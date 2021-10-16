using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace D2RAssist.Types
{
    public class XY
    {
        public int x;
        public int y;

        public Point ToPoint()
        {
            return new Point(x, y);
        }
    }

    public class RawAdjacentLevel
    {
        public XY[] exits;
        public XY origin;
        public int width;
        public int height;

        public AdjacentLevel ToInternal(Area area)
        {
            return new AdjacentLevel
            {
                Area = area,
                Origin = origin.ToPoint(),
                Exits = exits.Select(o => o.ToPoint()).ToArray(),
                Width = width,
                Height = height,
            };
        }
    }

    public class RawAreaData
    {
        public XY levelOrigin;
        public Dictionary<string, RawAdjacentLevel> adjacentLevels;
        public int[][] mapRows;
        public Dictionary<string, XY[]> npcs;
        public Dictionary<string, XY[]> objects;

        public AreaData ToInternal(Area area)
        {
            return new AreaData
            {
                Area = area,
                Origin = levelOrigin.ToPoint(),
                AdjacentLevels = adjacentLevels
                    .Select(o =>
                    {
                        var adjacentArea = Area.None;
                        if (int.TryParse(o.Key, out var parsed))
                        {
                            adjacentArea = (Area)parsed;
                        }

                        var level = o.Value.ToInternal(adjacentArea);
                        return (adjacentArea, level);
                    })
                    .Where(o => o.adjacentArea != Area.None)
                    .ToDictionary(k => k.adjacentArea, v => v.level),
                NPCs = npcs.Select(o =>
                    {
                        var positions = o.Value.Select(j => j.ToPoint()).ToArray();
                        var npc = NPC.Invalid;
                        if (int.TryParse(o.Key, out var parsed))
                        {
                            npc = (NPC)parsed;
                        }

                        return (npc, positions);
                    })
                    .Where(o => o.npc != NPC.Invalid)
                    .ToDictionary(k => k.npc, v => v.positions),
                Objects = objects.Select(o =>
                    {
                        var positions = o.Value.Select(j => j.ToPoint()).ToArray();
                        var gameObject = GameObject.NotApplicable;
                        if (int.TryParse(o.Key, out var parsed))
                        {
                            gameObject = (GameObject)parsed;
                        }

                        return (gameObject, positions);
                    })
                    .Where(o => o.gameObject != GameObject.NotApplicable)
                    .ToDictionary(k => k.gameObject, v => v.positions),
                CollisionGrid = mapRows
            };
        }
    }
}
