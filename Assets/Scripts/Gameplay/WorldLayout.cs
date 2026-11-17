using System.Collections.Generic;
using FishGame.Data;
using UnityEngine;

namespace FishGame.Gameplay
{
    /// <summary>
    /// 동심원 맵의 기하 정보. (R14 — 예전엔 구역 4개를 세로로 이어 붙인 맵이었다)
    ///
    ///   어항 = 가운데 원, 하수구 · 강 · 바다 = 그 바깥 고리. 한 구역이 끝나는 반지름은 이전 구역의 growth배(기본 8배).
    ///
    ///          ╭──────── 바다 (반지름 R0 × 8³) ────────╮
    ///          │   ╭──── 강 (R0 × 8²) ────╮           │
    ///          │   │  ╭ 하수구 (R0 × 8) ╮  │           │
    ///          │   │  │   ● 어항 (R0)    │  │           │
    ///
    /// 통로로 "나가는" 게 아니라 맵이 커진다: 지금 구역의 크기 조건을 채우면 벽(ActiveRadius)이
    /// 다음 구역 바깥 반지름까지 넓어진다 (RunManager가 애니메이션한다). 벽 바깥은 갈 수 없다.
    ///
    /// 벽 · 스폰 · 클램프 · 가치 계산이 전부 이 클래스 하나를 본다. 중심은 항상 (0, 0).
    /// </summary>
    public class WorldLayout
    {
        readonly ZoneData[] _zones;
        readonly float[] _outer;   // 구역 i의 바깥 반지름

        /// <summary>맵 끝(바다 바깥)의 재화가 어항보다 몇 배인지. GameDatabase.depthRichness.</summary>
        public float DepthRichness { get; private set; } = 1f;
        /// <summary>모든 재화에 곱해지는 전역 배율.</summary>
        public float GlobalValueScale { get; private set; } = 1f;
        /// <summary>구역 하나마다 반지름이 몇 배가 되는가.</summary>
        public float Growth { get; private set; } = 8f;
        /// <summary>어항(가운데 원)의 반지름.</summary>
        public float BaseRadius { get; private set; } = 24f;

        /// <summary>지금 벽의 반지름. 맵이 넓어지는 동안 RunManager가 조금씩 키운다.</summary>
        public float ActiveRadius { get; set; }

        public int ZoneCount => _zones.Length;
        public float MaxRadius => _outer[_outer.Length - 1];

        public ZoneData GetZone(int index) =>
            _zones.Length == 0 ? null : _zones[Mathf.Clamp(index, 0, _zones.Length - 1)];

        /// <summary>구역 i의 안쪽 반지름 (어항은 0).</summary>
        public float InnerRadius(int i) => i <= 0 ? 0f : _outer[Mathf.Min(i, _outer.Length) - 1];
        /// <summary>구역 i의 바깥 반지름 = 그 구역까지 넓어졌을 때의 벽.</summary>
        public float OuterRadius(int i) => _outer[Mathf.Clamp(i, 0, _outer.Length - 1)];

        WorldLayout(ZoneData[] zones, float[] outer)
        {
            _zones = zones;
            _outer = outer;
            ActiveRadius = outer[0];
        }

        // ══════════════════════════════════════════════════════════
        //  생성
        // ══════════════════════════════════════════════════════════
        public static WorldLayout Build(IList<ZoneData> zones, float depthRichness = 1f, float globalValueScale = 1f,
                                        float baseRadius = 24f, float growth = 8f)
        {
            var list = new List<ZoneData>();
            if (zones != null) foreach (var z in zones) if (z != null) list.Add(z);
            if (list.Count == 0) Debug.LogError("[WorldLayout] 존이 하나도 없습니다.");

            int n = Mathf.Max(1, list.Count);
            baseRadius = Mathf.Max(4f, baseRadius);
            growth = Mathf.Max(1.2f, growth);
            var outer = new float[n];
            for (int i = 0; i < n; i++) outer[i] = baseRadius * Mathf.Pow(growth, i);

            return new WorldLayout(list.Count > 0 ? list.ToArray() : new ZoneData[] { null }, outer)
            {
                DepthRichness = Mathf.Max(1f, depthRichness),
                GlobalValueScale = Mathf.Max(0.0001f, globalValueScale),
                Growth = growth,
                BaseRadius = baseRadius,
            };
        }

        // ══════════════════════════════════════════════════════════
        //  위치 → 구역
        // ══════════════════════════════════════════════════════════
        /// <summary>그 자리가 어느 고리(구역)에 있는가 — 배경 타일 · 빛 · 가치에 쓴다.</summary>
        public int ZoneIndexAt(Vector2 p)
        {
            float r = p.magnitude;
            for (int i = 0; i < _outer.Length; i++)
                if (r <= _outer[i]) return i;
            return _outer.Length - 1;
        }

        /// <summary>고리 안에서의 진행도. 0 = 안쪽 경계, 1 = 바깥 경계.</summary>
        public float DepthFactorInZone(Vector2 p)
        {
            int i = ZoneIndexAt(p);
            float inner = InnerRadius(i), outer = OuterRadius(i);
            return outer - inner <= 0.001f ? 0f : Mathf.Clamp01((p.magnitude - inner) / (outer - inner));
        }

        // ══════════════════════════════════════════════════════════
        //  벽
        // ══════════════════════════════════════════════════════════
        /// <summary>벽 안쪽으로 밀어 넣는다 (물리 콜라이더가 놓쳤을 때를 대비한 안전망).</summary>
        public Vector2 Clamp(Vector2 p, float radius)
        {
            float limit = Mathf.Max(0.1f, ActiveRadius - radius);
            float r = p.magnitude;
            return r <= limit ? p : (r < 0.0001f ? p : p / r * limit);
        }

        public bool Contains(Vector2 p, float radius) => p.magnitude <= ActiveRadius - radius;

        // ══════════════════════════════════════════════════════════
        //  가치
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 맵 전체에서의 진행도 0~1. 반지름이 구역마다 growth배라 로그로 잰다 —
        /// 어항 끝 = 0, 하수구 끝 = 1/3, 강 끝 = 2/3, 바다 끝 = 1 (구역 4개일 때).
        /// </summary>
        public float GlobalDepth01(Vector2 p)
        {
            if (_outer.Length <= 1) return 0f;
            float r = Mathf.Max(BaseRadius, p.magnitude);
            float steps = Mathf.Log(r / BaseRadius, Growth);
            return Mathf.Clamp01(steps / (_outer.Length - 1));
        }

        /// <summary>
        /// 이 자리에서 먹었을 때의 재화 배율 = 전역배율 × DepthRichness ^ (진행도) × 먹힌 물고기 구역의 보조 배율.
        /// 바깥 고리로 갈수록 연속으로 오른다.
        /// </summary>
        public float ValueMultiplierAt(Vector2 p, int trimZoneIndex = -1)
        {
            float byDepth = Mathf.Pow(DepthRichness, GlobalDepth01(p));
            int zi = trimZoneIndex >= 0 && trimZoneIndex < _zones.Length ? trimZoneIndex : ZoneIndexAt(p);
            var z = _zones[zi];
            float zoneTrim = z != null ? z.currencyMultiplier : 1f;
            return GlobalValueScale * byDepth * zoneTrim;
        }

        /// <summary>HUD에 보여줄 "지금 자리의 가치 배율" (전역 배율 제외).</summary>
        public float DepthValueDisplay(Vector2 p) => Mathf.Pow(DepthRichness, GlobalDepth01(p));

        // ══════════════════════════════════════════════════════════
        //  주요 지점
        // ══════════════════════════════════════════════════════════
        /// <summary>그 구역에서 판을 시작하는 자리 — 어항은 한가운데, 바깥 구역은 그 고리의 가운데쯤.</summary>
        public Vector2 SpawnPointForZone(int zoneIndex)
        {
            if (zoneIndex <= 0) return Vector2.zero;
            float r = Mathf.Lerp(InnerRadius(zoneIndex), OuterRadius(zoneIndex), 0.35f);
            float a = Random.value * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        /// <summary>다음 구역으로 넓어지는 단계가 남았는가 (마지막 구역이면 false).</summary>
        public bool HasGate(int zoneIndex) => zoneIndex < _zones.Length - 1;

        /// <summary>맵을 다음 구역까지 넓히는 데 필요한 크기. 마지막 구역이면 0.</summary>
        public float GateRequiredSize(int zoneIndex)
        {
            if (!HasGate(zoneIndex)) return 0f;
            var z = GetZone(zoneIndex);
            return z != null ? z.exitRequiredSize : 0f;
        }

        /// <summary>보스 구조물 자리 — 그 구역 고리 안쪽 경계 근처 (넓어지자마자 찾을 수 있게).</summary>
        public Vector2 BossStructureWorldPos(int zoneIndex, float angleRad)
        {
            float inner = InnerRadius(zoneIndex), outer = OuterRadius(zoneIndex);
            float r = Mathf.Lerp(inner, outer, 0.08f) + 20f;
            return new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * r;
        }
    }
}
