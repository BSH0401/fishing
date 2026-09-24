using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 존 4개를 세로로 이어 붙인 통합 맵의 기하 정보.
    ///
    /// 월드 좌표 규칙: 어항 천장이 y = 0, 아래로 내려갈수록 y가 작아진다(음수).
    /// "아래로 갈수록 깊다"가 좌표에 그대로 드러나도록 한 것이다.
    ///
    ///   y=0 ┬─────────────  어항 천장
    ///       │   어항 (사발)
    ///       ├──┐        ┌──  어항 바닥 + 통로 입구
    ///       │  │ 통로   │
    ///       ├──┘        └──  하수구 천장
    ///       │   하수구 (상자)
    ///       ⋮
    ///
    /// 벽 생성·스폰·클램프·가치 계산이 전부 이 클래스 하나를 본다.
    /// </summary>
    public class WorldLayout
    {
        public struct Slice
        {
            public ZoneData Zone;
            public int Index;

            /// <summary>존 본체의 위/아래 y</summary>
            public float YTop, YBottom;

            /// <summary>이 존 아래에 붙은 통로. HasCorridor일 때만 유효.</summary>
            public bool HasCorridor;
            public float CorridorTop, CorridorBottom;
            public float CorridorCenterX;
            public float CorridorHalfWidth;

            public float Height => YTop - YBottom;
        }

        readonly Slice[] _slices;

        /// <summary>맵 바닥의 재화가 맵 천장보다 몇 배인지. GameDatabase.depthRichness에서 받는다.</summary>
        public float DepthRichness { get; private set; } = 1f;

        /// <summary>모든 재화에 곱해지는 전역 배율. 총 플레이타임을 맞추는 손잡이.</summary>
        public float GlobalValueScale { get; private set; } = 1f;

        public float TopY { get; private set; }
        public float BottomY { get; private set; }
        public float TotalHeight => TopY - BottomY;
        public int ZoneCount => _slices.Length;

        public Slice GetSlice(int index) => _slices[Mathf.Clamp(index, 0, _slices.Length - 1)];
        public ZoneData GetZone(int index) => GetSlice(index).Zone;

        WorldLayout(Slice[] slices, float topY, float bottomY)
        {
            _slices = slices;
            TopY = topY;
            BottomY = bottomY;
        }

        // ══════════════════════════════════════════════════════════
        //  생성
        // ══════════════════════════════════════════════════════════
        public static WorldLayout Build(IList<ZoneData> zones,
                                        float depthRichness = 1f, float globalValueScale = 1f)
        {
            var list = new List<Slice>();
            float y = 0f;

            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z == null) continue;

                var s = new Slice
                {
                    Zone = z,
                    Index = list.Count,
                    YTop = y,
                    YBottom = y - Mathf.Max(10f, z.height),
                };
                y = s.YBottom;

                bool isLast = i == zones.Count - 1;
                if (z.hasExit && !isLast)
                {
                    s.HasCorridor = true;
                    s.CorridorTop = y;
                    s.CorridorBottom = y - Mathf.Max(2f, z.exitHeight);
                    s.CorridorCenterX = z.exitOffsetX;
                    s.CorridorHalfWidth = Mathf.Max(1f, z.exitHalfWidth);
                    y = s.CorridorBottom;
                }

                list.Add(s);
            }

            if (list.Count == 0)
            {
                Debug.LogError("[WorldLayout] 존이 하나도 없습니다.");
                var fallback = new Slice
                {
                    Zone = null, Index = 0, YTop = 0f, YBottom = -40f,
                };
                return new WorldLayout(new[] { fallback }, 0f, -40f);
            }

            return new WorldLayout(list.ToArray(), 0f, y)
            {
                DepthRichness = Mathf.Max(1f, depthRichness),
                GlobalValueScale = Mathf.Max(0.0001f, globalValueScale),
            };
        }

        // ══════════════════════════════════════════════════════════
        //  위치 → 존
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// y가 속한 존의 인덱스.
        /// 통로 안이라면 "아래쪽 존"으로 친다 — 게이트를 통과한 순간부터
        /// 다음 존에 들어온 것으로 취급해야 배경·스폰·가치가 한 박자 빠르게 전환된다.
        /// </summary>
        public int ZoneIndexAt(float y)
        {
            for (int i = 0; i < _slices.Length; i++)
            {
                var s = _slices[i];
                if (y <= s.YTop && y >= s.YBottom) return i;
                if (s.HasCorridor && y < s.CorridorTop && y >= s.CorridorBottom)
                    return Mathf.Min(i + 1, _slices.Length - 1);
            }
            return y > TopY ? 0 : _slices.Length - 1;
        }

        public bool IsInCorridor(float y, out int upperZoneIndex)
        {
            for (int i = 0; i < _slices.Length; i++)
            {
                var s = _slices[i];
                if (s.HasCorridor && y <= s.CorridorTop && y >= s.CorridorBottom)
                {
                    upperZoneIndex = i;
                    return true;
                }
            }
            upperZoneIndex = -1;
            return false;
        }

        // ══════════════════════════════════════════════════════════
        //  폭 / 중심
        // ══════════════════════════════════════════════════════════
        public float CenterXAt(float y)
        {
            if (IsInCorridor(y, out int upper)) return _slices[upper].CorridorCenterX;
            return 0f;
        }

        public float HalfWidthAt(float y)
        {
            if (IsInCorridor(y, out int upper)) return _slices[upper].CorridorHalfWidth;

            for (int i = 0; i < _slices.Length; i++)
            {
                var s = _slices[i];
                if (y > s.YTop || y < s.YBottom) continue;
                if (s.Zone == null) return 20f;
                float t = s.Height <= 0.001f ? 0f : (s.YTop - y) / s.Height;
                return s.Zone.HalfWidthAt(t);
            }

            // 범위 밖 — 가장 가까운 끝의 폭
            var edge = y > TopY ? _slices[0] : _slices[_slices.Length - 1];
            return edge.Zone != null ? edge.Zone.HalfWidthAt(y > TopY ? 0f : 1f) : 20f;
        }

        /// <summary>물리 콜라이더가 놓쳤을 때를 대비한 안전망. 벽 안쪽으로 밀어 넣는다.</summary>
        public Vector2 Clamp(Vector2 p, float radius)
        {
            p.y = Mathf.Clamp(p.y, BottomY + radius, TopY - radius);

            float cx = CenterXAt(p.y);
            float hw = HalfWidthAt(p.y);
            float limit = Mathf.Max(0.1f, hw - radius);
            p.x = Mathf.Clamp(p.x, cx - limit, cx + limit);
            return p;
        }

        public bool Contains(Vector2 p, float radius)
        {
            if (p.y > TopY - radius || p.y < BottomY + radius) return false;
            float cx = CenterXAt(p.y);
            float hw = HalfWidthAt(p.y);
            return Mathf.Abs(p.x - cx) <= hw - radius;
        }

        // ══════════════════════════════════════════════════════════
        //  깊이와 가치
        // ══════════════════════════════════════════════════════════
        /// <summary>존 안에서의 정규화 깊이. 0 = 존 천장, 1 = 존 바닥.</summary>
        public float DepthFactorInZone(float y)
        {
            int i = ZoneIndexAt(y);
            var s = _slices[i];
            if (s.Height <= 0.001f) return 0f;
            return Mathf.Clamp01((s.YTop - y) / s.Height);
        }

        /// <summary>
        /// 이 깊이에서 먹었을 때의 재화 배율.
        ///
        /// 예전에는 구역마다 손으로 잡은 배율 네 개를 썼는데, 그 값들이 서로 어긋나서
        /// 어항 → 하수구 구간이 거의 평평해지는 문제가 있었다.
        /// 지금은 맵 전체 깊이에 대한 지수 곡선 하나로 계산한다.
        ///
        ///     배율 = 전역배율 × DepthRichness ^ (수면에서 바닥까지의 진행도)
        ///
        /// 연속 함수라 구역 경계에서 값이 튀지 않고, 한 칸 내려갈 때마다 반드시 오른다.
        /// zone.currencyMultiplier는 특정 구역만 손볼 때 쓰는 보조 손잡이로 남겨 뒀다(기본 1).
        /// </summary>
        public float ValueMultiplierAt(float y, int trimZoneIndex = -1)
        {
            float depth01 = GlobalDepth01(y);
            float byDepth = Mathf.Pow(DepthRichness, depth01);

            // 보조 배율은 "먹힌 물고기가 속한 구역" 것을 쓴다(balance_sim과 같은 가정).
            // 위치로만 고르면 통로(= 아래 구역으로 침)에서 먹은 윗구역 물고기가
            // 아래 구역 배율을 받아 강 → 바다 통로에서 값이 2배로 뛴다.
            int zi = trimZoneIndex >= 0 && trimZoneIndex < _slices.Length ? trimZoneIndex : ZoneIndexAt(y);
            var z = _slices[zi].Zone;
            float zoneTrim = z != null ? z.currencyMultiplier : 1f;

            return GlobalValueScale * byDepth * zoneTrim;
        }

        /// <summary>HUD에 보여줄 "지금 깊이의 가치 배율" (전역 배율 제외 — 플레이어에겐 의미 없는 수라서).</summary>
        public float DepthValueDisplay(float y) => Mathf.Pow(DepthRichness, GlobalDepth01(y));

        /// <summary>월드 전체에서의 깊이 비율. HUD의 깊이 게이지에 쓴다.</summary>
        public float GlobalDepth01(float y)
        {
            if (TotalHeight <= 0.001f) return 0f;
            return Mathf.Clamp01((TopY - y) / TotalHeight);
        }

        // ══════════════════════════════════════════════════════════
        //  주요 지점
        // ══════════════════════════════════════════════════════════
        /// <summary>해당 존에서 플레이가 시작되는 위치 (천장에서 살짝 아래, 중앙).</summary>
        public Vector2 SpawnPointForZone(int zoneIndex)
        {
            var s = GetSlice(zoneIndex);
            float y = s.YTop - Mathf.Min(s.Height * 0.25f, 12f);
            return new Vector2(0f, y);
        }

        /// <summary>통로 입구(게이트)의 월드 위치. 게이트 오브젝트와 HUD 화살표가 쓴다.</summary>
        public Vector2 GateCenter(int zoneIndex)
        {
            var s = GetSlice(zoneIndex);
            if (!s.HasCorridor) return new Vector2(0f, s.YBottom);
            return new Vector2(s.CorridorCenterX, s.CorridorTop);
        }

        public bool HasGate(int zoneIndex) => GetSlice(zoneIndex).HasCorridor;

        /// <summary>해당 존의 통로를 열기 위해 필요한 크기. 통로가 없으면 0.</summary>
        public float GateRequiredSize(int zoneIndex)
        {
            var s = GetSlice(zoneIndex);
            if (!s.HasCorridor || s.Zone == null) return 0f;
            return s.Zone.exitRequiredSize;
        }

        public Vector2 BossStructureWorldPos(int zoneIndex)
        {
            var s = GetSlice(zoneIndex);
            if (s.Zone == null) return Vector2.zero;
            var local = s.Zone.bossStructureLocalPos;
            return new Vector2(local.x, s.YTop - Mathf.Abs(local.y));
        }
    }
}
