﻿/**
 *   Copyright (C) 2021 okaygo
 *
 *   https://github.com/misterokaygo/D2RAssist/
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
 **/

using D2RAssist.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace D2RAssist.Helpers
{
    public class Compositor
    {
        private readonly AreaData _areaData;
        private readonly Bitmap _background;
        private readonly Point _cropOffset;
        private readonly IReadOnlyList<PointOfInterest> _pointsOfInterest;
        private readonly Dictionary<(string, int), Font> _fontCache = new Dictionary<(string, int), Font>();

        private readonly Dictionary<(Shape, int, Color), Bitmap> _iconCache =
            new Dictionary<(Shape, int, Color), Bitmap>();

        public Compositor(AreaData areaData, IReadOnlyList<PointOfInterest> pointOfInterest)
        {
            _areaData = areaData;
            _pointsOfInterest = pointOfInterest;
            (_background, _cropOffset) = DrawBackground(areaData, pointOfInterest);
        }

        public Bitmap Compose(GameData gameData)
        {
            if (gameData.Area != _areaData.Area)
            {
                throw new ApplicationException("Asked to compose an image for a different area." +
                                               $"Compositor area: {_areaData.Area}, Game data: {gameData.Area}");
            }

            var image = (Bitmap)_background.Clone();

            using (var imageGraphics = Graphics.FromImage(image))
            {
                imageGraphics.CompositingQuality = CompositingQuality.HighQuality;
                imageGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                imageGraphics.SmoothingMode = SmoothingMode.HighQuality;
                imageGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var localPlayerPosition = gameData.PlayerPosition.OffsetFrom(_areaData.Origin).OffsetFrom(_cropOffset);

                if (Settings.Rendering.Player.CanDrawIcon())
                {
                    var playerIcon = GetIcon(Settings.Rendering.Player);
                    imageGraphics.DrawImage(playerIcon, localPlayerPosition);
                }

                // The lines are dynamic, and follow the player, so have to be drawn here.
                // The rest can be done in DrawBackground.
                foreach (var poi in _pointsOfInterest)
                {
                    if (poi.RenderingSettings.CanDrawLine())
                    {
                        var pen = new Pen(poi.RenderingSettings.LineColor, poi.RenderingSettings.LineThickness);
                        if (poi.RenderingSettings.CanDrawArrowHead())
                        {
                            pen.CustomEndCap = new AdjustableArrowCap(poi.RenderingSettings.ArrowHeadSize,
                                poi.RenderingSettings.ArrowHeadSize);
                        }

                        imageGraphics.DrawLine(pen, localPlayerPosition,
                            poi.Position.OffsetFrom(_areaData.Origin).OffsetFrom(_cropOffset));
                    }
                }
            }

            double biggestDimension = Math.Max(image.Width, image.Height);

            var multiplier = Settings.Map.Size / biggestDimension;

            if (multiplier == 0)
            {
                multiplier = 1;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (multiplier != 1)
            {
                image = ImageUtils.ResizeImage(image, (int)(image.Width * multiplier),
                    (int)(image.Height * multiplier));
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (Settings.Map.Rotate)
            {
                image = ImageUtils.RotateImage(image, 53, true, false, Color.Transparent);
            }

            return image;
        }

        private (Bitmap, Point) DrawBackground(AreaData areaData, IReadOnlyList<PointOfInterest> pointOfInterest)
        {
            var background = new Bitmap(areaData.CollisionGrid[0].Length, areaData.CollisionGrid.Length,
                PixelFormat.Format32bppArgb);
            using (var backgroundGraphics = Graphics.FromImage(background))
            {
                backgroundGraphics.FillRectangle(new SolidBrush(Color.Transparent), 0, 0,
                    areaData.CollisionGrid[0].Length,
                    areaData.CollisionGrid.Length);
                backgroundGraphics.CompositingQuality = CompositingQuality.HighQuality;
                backgroundGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                backgroundGraphics.SmoothingMode = SmoothingMode.HighQuality;
                backgroundGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                for (var y = 0; y < areaData.CollisionGrid.Length; y++)
                {
                    for (var x = 0; x < areaData.CollisionGrid[y].Length; x++)
                    {
                        var type = areaData.CollisionGrid[y][x];
                        var typeColor = Settings.Map.LookupMapColor(type);
                        if (typeColor != null)
                        {
                            background.SetPixel(x, y, (Color)typeColor);
                        }
                    }
                }

                foreach (var poi in pointOfInterest)
                {
                    if (poi.RenderingSettings.CanDrawIcon())
                    {
                        var icon = GetIcon(poi.RenderingSettings);
                        backgroundGraphics.DrawImage(icon, poi.Position.OffsetFrom(areaData.Origin));
                    }

                    if (!string.IsNullOrWhiteSpace(poi.Label) && poi.RenderingSettings.CanDrawLabel())
                    {
                        var font = GetFont(poi.RenderingSettings);
                        backgroundGraphics.DrawString(poi.Label, font,
                            new SolidBrush(poi.RenderingSettings.LabelColor),
                            poi.Position.OffsetFrom(areaData.Origin));
                    }
                }

                return ImageUtils.CropBitmap(background);
            }
        }

        private Font GetFont(PointOfInterestRenderingSettings poiSettings)
        {
            var cacheKey = (poiSettings.LabelFont, poiSettings.LabelFontSize);
            if (!_fontCache.ContainsKey(cacheKey))
            {
                var font = new Font(poiSettings.LabelFont,
                    poiSettings.LabelFontSize);
                _fontCache[cacheKey] = font;
            }

            return _fontCache[cacheKey];
        }

        private Bitmap GetIcon(PointOfInterestRenderingSettings poiSettings)
        {
            var cacheKey = (poiSettings.IconShape, poiSettings.IconSize, Color: poiSettings.IconColor);
            if (!_iconCache.ContainsKey(cacheKey))
            {
                var bitmap = new Bitmap(poiSettings.IconSize, poiSettings.IconSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    switch (poiSettings.IconShape)
                    {
                        case Shape.Ellipse:
                            g.FillEllipse(new SolidBrush(poiSettings.IconColor), 0, 0, poiSettings.IconSize,
                                poiSettings.IconSize);
                            break;
                        case Shape.Rectangle:
                            g.FillRectangle(new SolidBrush(poiSettings.IconColor), 0, 0, poiSettings.IconSize,
                                poiSettings.IconSize);
                            break;
                    }
                }

                _iconCache[cacheKey] = bitmap;
            }

            return _iconCache[cacheKey];
        }
    }
}
