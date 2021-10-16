/**
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
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace D2RAssist.Helpers
{
    public class MapApi : IDisposable
    {
        private readonly string _endpoint;
        private readonly string _sessionId;
        private readonly ConcurrentDictionary<Area, AreaData> _cache;
        private readonly BlockingCollection<Area[]> _prefetchRequests;
        private readonly Thread _thread;

        public static MapApi Create(string endpoint, Difficulty difficulty, uint mapSeed)
        {
            var sessionId = CreateSession(endpoint, difficulty, mapSeed);
            return new MapApi(endpoint, sessionId);
        }

        private static string CreateSession(string endpoint, Difficulty difficulty, uint mapSeed)
        {
            var values = new Dictionary<string, uint>
            {
                { "difficulty", (uint)difficulty },
                { "mapid", mapSeed }
            };

            using (var client = new HttpClient())
            {
                var json = JsonConvert.SerializeObject(values);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = client.PostAsync(endpoint + "sessions/", content).GetAwaiter().GetResult();
                var session =
                    JsonConvert.DeserializeObject<MapApiSession>(response.Content.ReadAsStringAsync().GetAwaiter()
                        .GetResult());
                return session.id;
            }
        }

        private static void DestroySession(string endpoint, string sessionId)
        {
            using (var client = new HttpClient())
            {
                var response =
                    client.DeleteAsync(endpoint + "sessions/" + sessionId).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
            }
        }

        private MapApi(string endpoint, string sessionId)
        {
            _endpoint = endpoint;
            _sessionId = sessionId;
            // Cache for pre-fetching maps for the surrounding areas.
            _cache = new ConcurrentDictionary<Area, AreaData>();
            _prefetchRequests = new BlockingCollection<Area[]>();
            _thread = new Thread(Prefetch);
            _thread.Start();

            if (Settings.Map.PrefetchAreas.Any())
            {
                _prefetchRequests.Add(Settings.Map.PrefetchAreas);
            }
        }

        public AreaData GetMapData(Area area)
        {
            if (!_cache.TryGetValue(area, out var areaData))
            {
                // Not in the cache, block.
                Console.WriteLine($"Cache miss on {area}");
                areaData = GetMapDataInternal(area);
            }

            var adjacentAreas = areaData.AdjacentLevels.Keys.ToArray();
            if (adjacentAreas.Any())
            {
                _prefetchRequests.Add(adjacentAreas);
            }

            return areaData;
        }

        private void Prefetch()
        {
            while (true)
            {
                var areas = _prefetchRequests.Take();
                if (Settings.Map.ClearPrefetchedOnAreaChange)
                {
                    _cache.Clear();
                }

                // Special value telling us to exit.
                if (areas.Length == 0)
                {
                    Console.WriteLine("Prefetch thread terminating");
                    return;
                }

                foreach (var area in areas)
                {
                    if (_cache.ContainsKey(area)) continue;

                    _cache[area] = GetMapDataInternal(area);
                    Console.WriteLine($"Prefetched {area}");
                }
            }
        }

        private AreaData GetMapDataInternal(Area area)
        {
            using (var client = new HttpClient())
            {
                var response = client.GetAsync(_endpoint + "sessions/" + _sessionId +
                                               "/areas/" + (uint)area).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                var rawMapData =
                    JsonConvert.DeserializeObject<RawAreaData>(response.Content.ReadAsStringAsync().GetAwaiter()
                        .GetResult());
                return rawMapData.ToInternal(area);
            }
        }

        public void Dispose()
        {
            _prefetchRequests.Add(new Area[] { });
            _thread.Join();
            DestroySession(_endpoint, _sessionId);
        }

        private class MapApiSession
        {
            public string id;
            public uint difficulty;
            public uint mapId;
        }
    }
}