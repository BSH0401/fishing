"""
물고기 인크리멘탈 게임 밸런스 시뮬레이터 (통합 맵 판).

맵 4개가 세로로 이어진 하나의 맵이 되었다.
한 판은 "도달해 본 가장 깊은 구역에서 시작해, 먹어서 커지면 통로가 열리고,
더 깊이 내려가 더 값진 물고기를 먹는다"로 바뀌었다.
목표: 총 플레이타임 4~5시간 안에 바다의 보스까지.

스킬트리는 기획 도면을 옮긴 칸 231개짜리 그래프다 (skill_tree.json).
칸 하나 = 1회 구매, 이웃 칸 중 하나가 찍혀 있어야 열린다.
구매 AI는 "찍힌 영역에서 뻗는 경로 중 효용/비용이 가장 좋은 경로"의 첫 칸을 산다.

코스트는 칸별이 아니라 "지금까지 찍은 총 칸 수"로 정해진다.
    N번째 칸 = round(1.10^e(N)), e(N) = N-1 (N ≤ 100), 이후 성장률 1.0 (정체 구간은 +1 보정)

python3 balance_sim.py              → 상세 리포트 (클리어 + 풀트리)
python3 balance_sim.py --trials 60  → 60회 반복 분포
python3 balance_sim.py --clear      → 클리어 시점에 무엇을 찍었나
python3 balance_sim.py --full       → 풀트리 상태 점검
python3 balance_sim.py --curve      → 코스트 곡선 확인
"""
import math, random, sys, statistics

# ══════════════════════════════════════════════════════════════
#  코스트 곡선 (SkillNode.cs의 SkillCostCurve와 동일해야 함)
# ══════════════════════════════════════════════════════════════
# 노드가 231개로 늘어 한 가지 성장률로는 "클리어 4.5시간"과 "풀트리 달성"을 동시에 못 맞춘다.
# 그래서 SOFTCAP번째 노드부터 성장률을 낮춘다 — 클리어 전엔 가파르게, 그 뒤엔 완만하게.
#     지수 e(N) = N-1                         (N ≤ SOFTCAP)
#               = SOFTCAP-1 + (N-SOFTCAP)·r    (N > SOFTCAP),  r = ln(G2)/ln(G1)
#     가격 = round(G1^e(N)), 정체되면 +1
GROWTH = 1.10          # G1
GROWTH_LATE = 1.00     # G2 — 1이면 SOFTCAP 이후 가격이 사실상 고정 (+1씩)
SOFTCAP = 100
_COST_CACHE = []


def set_growth(g, g_late=None, softcap=None):
    global GROWTH, GROWTH_LATE, SOFTCAP, _COST_CACHE
    GROWTH = g
    GROWTH_LATE = g if g_late is None else g_late
    if softcap is not None:
        SOFTCAP = softcap
    _COST_CACHE = []


def _exponent(n):
    if n <= SOFTCAP:
        return n - 1
    return (SOFTCAP - 1) + (n - SOFTCAP) * (math.log(GROWTH_LATE) / math.log(GROWTH))


def cost_of_node(n):
    """N번째(1부터)로 찍는 노드의 가격."""
    global _COST_CACHE
    if not _COST_CACHE:
        prev = 0.0
        cache = [0.0]
        for i in range(1, 1201):
            cur = float(round(GROWTH ** _exponent(i)))
            if cur <= prev:
                cur = prev + 1.0
            cache.append(cur)
            prev = cur
        _COST_CACHE = cache
    if n < 1:
        n = 1
    return _COST_CACHE[n] if n < len(_COST_CACHE) else float(round(GROWTH ** _exponent(n)))


def cumulative_cost(count):
    return sum(cost_of_node(i) for i in range(1, count + 1))


# ══════════════════════════════════════════════════════════════
#  기본 스탯 (GameDatabase와 동일하게 유지할 것)
# ══════════════════════════════════════════════════════════════
BASE_SIZE   = 1.0
BASE_TIME   = 30.0
BASE_SPEED  = 6.5
MAX_SPEED   = 24.0
SPEED_EXP   = 0.5          # 크기 배율 → 속도 배율 지수
DRAIN_ACCEL_PER_60S = 2.2
MAX_DRAIN = 10.0
MENU_SECONDS_PER_RUN = 10.0
DEATH_PENALTY = 0.30

# ══════════════════════════════════════════════════════════════
#  스킬트리 — 기획 도면(draw.io)을 그대로 옮긴 그래프 (skill_tree.json)
#  도형 하나 = 노드 하나 = 1회 구매. 선으로 이어진 이웃 중 하나라도 찍혀 있으면 열린다.
#  같은 종류 노드를 k개 찍으면 그 종류의 "레벨"이 k다.
# ══════════════════════════════════════════════════════════════
import json, os, heapq
_TREE = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "skill_tree.json"), encoding="utf-8"))
SLOTS = _TREE["slots"]                 # [{id, type, x, y}]
ROOT = _TREE["root"]
ADJ = [[] for _ in SLOTS]
for a, b in _TREE["edges"]:
    ADJ[a].append(b); ADJ[b].append(a)
SLOT_COUNT = len(SLOTS) - 1            # 시작점 제외

# 종류 정의 : (id, 계산 방식, 노드당 값, 코스트 배율)
#     mult = 복리(×(1+v)), add = 가산, unlock = 해금
TYPES = [
    ("unlock_booster", "unlock", 1,     3.0),
    ("unlock_vacuum",  "unlock", 1,     5.0),
    ("unlock_armor",   "unlock", 1,     6.0),
    ("unlock_bait",    "unlock", 1,     6.0),
    ("unlock_volt",    "unlock", 1,     8.0),
    ("unlock_missile", "unlock", 1,    10.0),

    ("battery",  "add",  1.00,  1.0),   # 커다란 배터리 : 제한시간 +1초
    ("teeth",    "mult", 0.02,  1.0),   # 치아 교정     : 입·흡입력 +2%
    ("camera",   "mult", 0.035, 1.0),   # 카메라 장착   : 시야 +3.5%
    ("cell",     "mult", 0.025, 1.0),   # 물고기 전지   : 시간 회복 +2.5%
    ("acid",     "mult", 0.04,  1.2),   # 위액 산성도   : 재화 +4%
    ("armor",    "mult", 0.10,  2.0),   # 덧붙인 장갑   : 크기·속도 +10% (진행의 축)

    ("booster_range", "mult", 0.50, 1.0),
    ("booster_power", "unlock", 1,  6.0),
    ("vacuum_range",  "mult", 0.60, 1.0),
    ("armor_stack",   "add",  1.00, 3.0),
    ("bait_range",    "mult", 0.50, 1.0),
    ("bait_count",    "add",  1.00, 2.0),
    ("volt_power",    "mult", 0.60, 1.0),
    ("missile_power", "mult", 0.60, 2.0),
]
TYPE_BY_ID = {t[0]: t for t in TYPES}
MAX_LEVEL = {t[0]: sum(1 for s in SLOTS if s["type"] == t[0]) for t in TYPES}

# 그리디 구매용 효용 — 실제 플레이어가 대충 무엇을 먼저 원하나
UTILITY = {
    "armor": 50, "acid": 14, "teeth": 6, "cell": 5, "camera": 2.5, "battery": 8,
    "unlock_missile": 60, "unlock_vacuum": 45, "unlock_armor": 35, "unlock_booster": 30,
    "unlock_bait": 30, "unlock_volt": 25, "booster_power": 20,
    "booster_range": 6, "vacuum_range": 6, "bait_range": 5, "volt_power": 6,
    "missile_power": 10, "armor_stack": 12, "bait_count": 6,
}

# ══════════════════════════════════════════════════════════════
#  맵 : (이름, 재화배율, 보스게이트, 필드 평균 size, 평균 재화, 평균 시간보상)
# ══════════════════════════════════════════════════════════════
# 구역: (이름, 보조배율, 통로필요크기, 필드평균size, 평균재화, 평균시간, 최소어size, 세로길이, 통로길이)
# 평균값은 ContentGenerator의 스폰 테이블(가중치 34/26/20/13/7 + 희귀어 2.5)의 가중평균이다.
# 구역 배율은 구간 페이싱을 잡는 손잡이다(tune.py가 정한다).
# 깊이에 따른 매끄러운 상승은 아래 DEPTH_RICHNESS 곡선이 따로 맡는다.
ZONES = [
    ("어항",   0.428621,  3.1,  0.877,  0.385, 1.029, 0.30,  46.0, 16.0),
    ("하수구", 0.516234,  8.1,  2.866,  1.566, 1.234, 1.00, 110.0, 22.0),
    ("강",     0.197919, 21.0,  7.676,  6.528, 1.537, 2.70, 260.0, 40.0),
    ("바다",   0.209360,  0.0, 17.800, 19.640, 1.740, 6.50, 520.0,  0.0),
]

# ── 종별 데이터 — ContentGenerator.cs에서 그대로 읽는다 (손으로 옮기면 어긋난다) ──
#   SPECIES[z] = [(가중치, size, money, time, 도감 랭크 or None), ...]
import re as _re
def _load_species():
    src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Editor",
                            "ContentGenerator.cs"), encoding="utf-8").read()
    def fish(block):
        out = []
        for m in _re.finditer(r'F\("(Fish_[^"]+)",\s*"[^"]*",\s*([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f', block):
            out.append((m.group(1), float(m.group(2)), float(m.group(3)), float(m.group(4))))
        return out
    map_block = src[src.index("MapFish ="):src.index("Rares =")]
    rare_block = src[src.index("Rares ="):src.index("static readonly FishDef Boss")]
    maps = [fish(b) for b in map_block.split("new[]")[1:]]
    rares = fish(rare_block)
    weights = [34, 26, 20, 13, 7]
    table = []
    for z, fl in enumerate(maps):
        rows = [(weights[i], f[1], f[2], f[3], i) for i, f in enumerate(fl)]
        if z < len(rares):
            rows.append((2.5, rares[z][1], rares[z][2], rares[z][3], None))
        table.append(rows)
    return table

SPECIES = _load_species()

# 먹이를 고를 때 큰 것을 얼마나 선호하나 (1 = 가중치 × 크기에 비례 — 실제 플레이어는 큰 걸 노린다)
PREY_SIZE_PREFERENCE = 1.0


def edible_fraction(zone, size, limit_mult=1.0):
    rows = SPECIES[zone]
    tw = sum(r[0] for r in rows)
    return sum(r[0] for r in rows if r[1] <= size * limit_mult) / tw


def pick_prey(zone, size, rng, limit_mult=1.0):
    """먹을 수 있는(크기 ≤ 내 크기×limit) 종 중 하나. 없으면 None."""
    rows = [r for r in SPECIES[zone] if r[1] <= size * limit_mult]
    if not rows:
        return None
    ws = [r[0] * (r[1] ** PREY_SIZE_PREFERENCE) for r in rows]
    x = rng.random() * sum(ws)
    for r, w in zip(rows, ws):
        x -= w
        if x <= 0:
            return r
    return rows[-1]


# 바다는 깊이 520으로 넓고 보스 구조물이 46% 지점에 있어, 실제로는 바닥까지 잘 안 내려간다
OCEAN_DEPTH_CAP = 0.6
BOSS_DEPTH01 = 0.46

# ── 깊이 ↔ 가치 (WorldLayout.ValueMultiplierAt와 같은 식) ──────
#     배율 = GLOBAL_VALUE_SCALE × DEPTH_RICHNESS ^ (수면→바닥 진행도)
# 연속 함수라 구역 경계에서 값이 튀지 않고, 내려갈수록 반드시 오른다.
GLOBAL_VALUE_SCALE = 1.0
DEPTH_RICHNESS = 3.0

WORLD_HEIGHT = sum(z[7] + z[8] for z in ZONES)


def zone_top_depth(idx):
    """구역 천장이 수면에서 얼마나 깊은지."""
    return sum(ZONES[i][7] + ZONES[i][8] for i in range(idx))


def value_multiplier(zone_idx, depth01_in_zone):
    """그 구역의 주어진 깊이에서의 재화 배율."""
    depth = zone_top_depth(zone_idx) + ZONES[zone_idx][7] * depth01_in_zone
    g = min(1.0, depth / WORLD_HEIGHT) if WORLD_HEIGHT > 0 else 0.0
    return GLOBAL_VALUE_SCALE * (DEPTH_RICHNESS ** g) * ZONES[zone_idx][1]


# 바다 보스 — 이걸 먹으면 클리어. 통로가 아니라 크기로 직접 판정한다.
BOSS_SIZE = 43.0
BOSS_MONEY = 260.0

# 통로를 통과하는 데 드는 시간(초)
DESCEND_SECONDS = 6.0

# 구역에 들어온 뒤 이만큼 지나면 그 구역 바닥까지 내려간 것으로 본다
DEPTH_RAMP_SECONDS = 25.0

# 히든 아이템 — 구역당 2개, 총 8개. 하나당 재화 +5% (가산, 영구)
HIDDEN_TOTAL = 8
HIDDEN_BONUS = 0.05

BOSS_REWARD_MULT = 12

# ── 인게임 성장 (먹을수록 커진다) ──────────────────────────────
# 질량 보존식: 새 크기 = √(내 크기² + 먹이 크기² × 효율)
GROWTH_ENABLED = True
GROWTH_EFFICIENCY = 0.15
GROWTH_MAX_MULT = 2.5

# ── 상한 (GameDatabase와 동일) ─────────────────────────────────
MAX_PLAYER_SIZE = 56.0        # 절대 크기 상한
MAX_MOUTH_MULT = 2.6          # 치아 교정 효과 상한
# 액티브 강화는 가산으로 쌓인다 (복리 아님) — PlayerStats.Build와 같게
LINEAR_NODES = {"booster_range", "vacuum_range", "bait_range", "volt_power", "missile_power"}
# 구역 출구로 빠져나갈 수 있는 최대 몸 크기 = 통로 반폭 × 2 × 0.9
ZONE_EXIT_HALF_WIDTH = [7.0, 15.0, 32.0, None]


def zone_size_cap(zone_idx):
    hw = ZONE_EXIT_HALF_WIDTH[zone_idx]
    return MAX_PLAYER_SIZE if hw is None else min(MAX_PLAYER_SIZE, hw * 2 * 0.9)

# ── 물고기 도감 ────────────────────────────────────────────────
# 종을 100마리 먹을 때마다 그 종의 효과가 한 단계씩 (최대 10단계)
CODEX_MILESTONE = 100
CODEX_MAX_TIERS = 10
SPAWN_WEIGHTS = [34, 26, 20, 13, 7]
# 맵 안 크기 순서대로 배정된 효과 (ContentGenerator.CodexByRank와 동일)
CODEX_BY_RANK = [
    ("money", 0.020),
    ("time",  0.020),
    ("mouth", 0.018),
    ("speed", 0.015),
    ("maxt",  0.60),
]


class Build:
    def __init__(self):
        self.lv = {t[0]: 0 for t in TYPES}
        self.bought = [False] * len(SLOTS)
        self.bought[ROOT] = True
        self.nodes_bought = 0
        self._step = None
        # 도감: (구역, 랭크) → 먹은 수
        self.codex = {}
        # 구석에서 주운 히든 아이템 수 (영구 재화 보너스)
        self.hidden = 0
        # 지금까지 내려가 본 가장 깊은 구역 — 다음 판의 시작 구역이 된다
        self.deepest = 0

    def add_codex_kills(self, map_idx, kills):
        """한 판에서 먹은 수를 스폰 가중치대로 종별로 나눠 누적한다."""
        total_w = sum(SPAWN_WEIGHTS)
        for rank, w in enumerate(SPAWN_WEIGHTS):
            key = (map_idx, rank)
            self.codex[key] = self.codex.get(key, 0.0) + kills * w / total_w

    def codex_bonus(self, kind):
        """
        도감 보너스 (PlayerStats.ApplyCodexBonuses와 같게):
        재화·시간·입은 종마다 곱하고, 속도·최대시간은 더한다. 반환값은 '배율 - 1' 또는 합.
        """
        mult, add = 1.0, 0.0
        for (map_idx, rank), eaten in self.codex.items():
            btype, value = CODEX_BY_RANK[rank]
            if btype != kind:
                continue
            tiers = min(int(eaten // CODEX_MILESTONE), CODEX_MAX_TIERS)
            if kind in ("money", "time", "mouth"):
                mult *= 1.0 + value * tiers
            else:
                add += value * tiers
        return (mult - 1.0) if kind in ("money", "time", "mouth") else add

    def add_kill(self, zone, rank):
        if rank is None:
            return
        key = (zone, rank)
        self.codex[key] = self.codex.get(key, 0.0) + 1.0

    # ── 스탯 ───────────────────────────────────────────────
    def mult(self, tid):
        v = TYPE_BY_ID[tid][2]
        if tid in LINEAR_NODES:
            return 1.0 + v * self.lv[tid]
        return (1.0 + v) ** self.lv[tid]

    def has(self, tid):
        return self.lv[tid] > 0

    @property
    def armor_levels(self): return self.lv["armor"]
    @property
    def size(self):      return min(MAX_PLAYER_SIZE, BASE_SIZE * (1.10 ** self.armor_levels))
    @property
    def speed(self):
        base = BASE_SPEED * ((1.10 ** self.armor_levels) ** SPEED_EXP)
        return min(MAX_SPEED * 1.5, min(MAX_SPEED, base) * (1 + self.codex_bonus("speed")))
    @property
    def max_time(self):  return BASE_TIME + self.lv["battery"] + self.codex_bonus("maxt")
    @property
    def mouth(self):     return min(MAX_MOUTH_MULT, self.mult("teeth") * (1 + self.codex_bonus("mouth")))
    @property
    def time_mult(self): return self.mult("cell") * (1 + self.codex_bonus("time"))
    @property
    def money_mult(self):
        return (self.mult("acid")
                * (1 + self.codex_bonus("money"))
                * (1 + HIDDEN_BONUS * self.hidden))
    @property
    def armor(self):     return (1 + self.lv["armor_stack"]) if self.has("unlock_armor") else 0

    # ── 트리 ───────────────────────────────────────────────
    def available(self, i):
        """아직 안 찍었고, 이웃 중 하나가 찍혀 있다."""
        return not self.bought[i] and any(self.bought[n] for n in ADJ[i])

    def price_of_type(self, tid):
        return math.ceil(round(cost_of_node(self.nodes_bought + 1) * TYPE_BY_ID[tid][3], 6))

    def price(self, i):
        return self.price_of_type(SLOTS[i]["type"])

    def buy(self, i):
        self._step = None
        self.bought[i] = True
        self.lv[SLOTS[i]["type"]] += 1
        self.nodes_bought += 1

    def utility(self, tid):
        u = UTILITY[tid]
        # 상한에 걸려 더 찍어도 의미 없는 것은 낮춘다
        if tid == "teeth" and self.mouth >= MAX_MOUTH_MULT - 1e-6:
            u = 0.3
        if tid == "armor" and self.size >= MAX_PLAYER_SIZE - 1e-6:
            u = 0.3
        return u

    def best_path_step(self):
        # 찍은 게 바뀌지 않았으면 답도 같다 (도감으로 입 배율이 상한에 닿는 경우만 살짝 늦게 반영)
        if self._step is None:
            self._step = self._best_path_step()
        return self._step

    def _best_path_step(self):
        """
        찍힌 영역에서 뻗어나가는 경로 중 '효용 합 / 코스트 배율 합'이 가장 좋은 경로의 첫 노드.
        목표가 멀리 있으면 그 길에 놓인 노드부터 찍는다 — 실제 플레이어가 트리를 뚫는 방식.
        """
        INF = float("inf")
        cost = [INF] * len(SLOTS)
        util = [0.0] * len(SLOTS)
        first = [-1] * len(SLOTS)
        pq = []
        for i in range(len(SLOTS)):
            if self.available(i):
                t = SLOTS[i]["type"]
                cost[i] = TYPE_BY_ID[t][3]
                util[i] = self.utility(t)
                first[i] = i
                heapq.heappush(pq, (cost[i], i))
        best, best_score = -1, -1.0
        while pq:
            c, u = heapq.heappop(pq)
            if c > cost[u]:
                continue
            score = util[u] / cost[u]
            if score > best_score:
                best, best_score = first[u], score
            for n in ADJ[u]:
                if self.bought[n]:
                    continue
                t = SLOTS[n]["type"]
                nc = c + TYPE_BY_ID[t][3]
                if nc < cost[n]:
                    cost[n] = nc
                    util[n] = util[u] + self.utility(t)
                    first[n] = first[u]
                    heapq.heappush(pq, (nc, n))
        return best


def eat_interval(build, fish_size, size=None, zone=None):
    """평균 포식 간격(초). size를 주면 인런 성장한 크기를 쓴다."""
    if size is None:
        size = build.size
    if zone is not None:
        edible = min(0.94, max(0.02, edible_fraction(zone, size)))
    else:
        edible = min(0.94, max(0.02, 0.55 * (size / fish_size)))
    reach = (build.speed / BASE_SPEED) * (build.mouth ** 0.6)
    itv = 1.9 / (reach * edible)

    if build.has("unlock_vacuum"):
        itv *= 0.80 / (build.mult("vacuum_range") ** 0.15)
    if build.has("unlock_bait"):
        itv *= 0.86 / ((build.mult("bait_range") * (1 + build.lv["bait_count"] * 0.15)) ** 0.12)
    if build.has("unlock_booster"):
        itv *= 0.93
    if build.has("unlock_volt"):
        itv *= 0.90 / (build.mult("volt_power") ** 0.12)
    return max(0.9, min(20.0, itv))


def missile_kills_per_second(build, fish_size, size=None):
    """미사일은 간격과 별개로 직접 처치를 만든다."""
    if not build.has("unlock_missile"):
        return 0.0
    if size is None:
        size = build.size
    count = 1 + build.lv["missile_power"]
    interval = 6.0
    hit_rate = 0.55 if size * 3.0 >= fish_size else 0.15
    return count * hit_rate / interval


def pick_start_zone(build):
    """
    시작 구역 선택.
    도달해 본 가장 깊은 구역이 기본이지만, 기본 크기가 그 구역에서 먹고 살
    수준이 안 되면 한 칸 위에서 시작한다 — 실제 플레이어가 하는 판단이다.
    (통로는 '판 중에 커진 크기'로 열리므로, 도달했을 때보다 기본 크기가 작을 수 있다)
    """
    z = build.deepest
    while z > 0:
        avg_size = ZONES[z][3]
        if build.size >= avg_size * 0.42:
            break
        z -= 1
    return z


def simulate_run(build, rng):
    """
    한 판. 시작 구역에서 출발해 먹고 커지며 아래로 내려간다.
    반환: (재화, 소요초, 도달한 가장 깊은 구역, 보스 처치 여부)
    """
    zone = pick_start_zone(build)

    t = build.max_time
    elapsed = 0.0
    money = 0.0
    armor = build.armor

    # 인게임 성장: 판 시작 크기에서 출발해 먹을수록 커진다
    size = build.size
    run_cap = size * GROWTH_MAX_MULT if GROWTH_ENABLED else size

    deepest = zone
    time_in_zone = 0.0
    kills_in_zone = 0.0

    while t > 0 and elapsed < 1800:
        name, _trim, gate, fish_size, fish_money, fish_time, min_size, _h, _eh = ZONES[zone]

        # ── 아래로 ──
        # 통로는 '지금 크기'로 열린다. 열렸으면 내려간다 — 아래가 항상 더 값지다.
        if zone < len(ZONES) - 1 and size >= gate:
            drain = min(MAX_DRAIN, 1.0 + (elapsed / 60.0) * DRAIN_ACCEL_PER_60S)
            t -= DESCEND_SECONDS * drain
            elapsed += DESCEND_SECONDS
            zone += 1
            deepest = max(deepest, zone)
            time_in_zone = 0.0
            kills_in_zone = 0.0
            continue

        # ── 보스 ──
        # 바다에서 보스 크기를 넘기면 구조물을 깨고 삼킨다. 클리어.
        if zone == len(ZONES) - 1 and size >= BOSS_SIZE:
            money += BOSS_MONEY * value_multiplier(zone, BOSS_DEPTH01) * build.money_mult
            return money, elapsed + 12.0, deepest, True

        itv = eat_interval(build, fish_size, size, zone)
        mps = missile_kills_per_second(build, fish_size, size)

        drain = min(MAX_DRAIN, 1.0 + (elapsed / 60.0) * DRAIN_ACCEL_PER_60S)
        t -= itv * drain
        elapsed += itv
        time_in_zone += itv
        if t <= 0:
            break

        # 위험 — 내가 클수록, 비늘이 남아 있을수록 안전.
        danger = max(0.0, min(0.05, 0.016 * (fish_size * 2.2 - size) / max(1.0, fish_size)))
        if rng.random() < danger * itv:
            if armor > 0:
                armor -= 1
            else:
                return money * (1 - DEATH_PENALTY), elapsed, deepest, False

        # 깊이가 값을 정한다 — 맵 전체에 걸친 연속 곡선
        depth01 = min(1.0, time_in_zone / DEPTH_RAMP_SECONDS)
        if zone == len(ZONES) - 1:
            depth01 = min(depth01, OCEAN_DEPTH_CAP)
        depth_mult = value_multiplier(zone, depth01)

        # 실제로 먹은 종의 값으로 계산한다
        # (예전엔 구역 평균 × 크기 보너스(최대 3배)라 수입을 2~3배 부풀렸다)
        preys = []
        prey = pick_prey(zone, size, rng)
        if prey is not None:
            preys.append(prey)
        n_missile = mps * itv            # 미사일은 내 크기 3배까지 잡는다 (보스 제외)
        while n_missile > 0:
            if rng.random() < min(1.0, n_missile):
                m = pick_prey(zone, size, rng, limit_mult=3.0)
                if m is not None:
                    preys.append(m)
            n_missile -= 1.0

        for w, fsize, fmoney, ftime, rank in preys:
            money += fmoney * build.money_mult * depth_mult
            t = min(build.max_time, t + ftime * build.time_mult)
            build.add_kill(zone, rank)
            if GROWTH_ENABLED:
                max_size = max(build.size, min(run_cap, zone_size_cap(zone)))
                if size < max_size:
                    size = min(max_size, (size * size + fsize * fsize * GROWTH_EFFICIENCY) ** 0.5)

    return money, elapsed, deepest, False


def greedy_buy(build, currency):
    """
    실제 플레이어의 구매 패턴 근사.
    찍힌 영역에서 뻗는 경로 중 가장 효율 좋은 경로를 골라 그 첫 노드를 산다.
    살 돈이 없으면 모은다 (잡템을 사면 다음 노드 값이 올라 목표가 멀어진다).
    """
    while True:
        step = build.best_path_step()
        if step < 0:
            return currency
        p = build.price(step)
        if p > currency:
            return currency
        build.buy(step)
        currency -= p


def play_through(seed, post_clear_hours=0.0):
    """
    게임 전체를 돌린다. post_clear_hours > 0이면 클리어 후에도 계속해
    트리를 전부 채우는 데 걸리는 시간까지 잰다.
    반환: build, runs, 클리어까지 초, milestones, phases, 풀트리 누적 h(못 채우면 None)
    """
    rng = random.Random(seed)
    build = Build()
    currency, total_seconds, runs = 0.0, 0.0, 0

    milestones = []                 # (구역, 누적 런, 누적 시간h)
    phases = []                     # (구역, 구간 런, 구간 분)
    phase_start = (0, 0.0)
    cleared = False
    clear_seconds = None
    full_hours = None

    while runs < 20000:
        money, elapsed, deepest, boss = simulate_run(build, rng)
        runs += 1
        currency += money
        total_seconds += elapsed + MENU_SECONDS_PER_RUN

        # 새 구역 도달 — 죽어도 남는다
        if deepest > build.deepest:
            build.deepest = deepest
            milestones.append((deepest, runs, total_seconds / 3600))
            phases.append((deepest, runs - phase_start[0], (total_seconds - phase_start[1]) / 60))
            phase_start = (runs, total_seconds)

        # 히든 아이템 — 도달한 구역마다 2개, 판을 거듭하며 차츰 줍는다
        reachable = min(HIDDEN_TOTAL, (build.deepest + 1) * 2)
        if build.hidden < reachable and runs % 4 == 0:
            build.hidden += 1

        if boss and not cleared:
            cleared = True
            clear_seconds = total_seconds
            milestones.append((-1, runs, total_seconds / 3600))
            phases.append((-1, runs - phase_start[0], (total_seconds - phase_start[1]) / 60))
            if post_clear_hours <= 0:
                break

        currency = greedy_buy(build, currency)

        if cleared:
            if build.nodes_bought >= SLOT_COUNT:
                full_hours = total_seconds / 3600
                break
            if total_seconds - clear_seconds > post_clear_hours * 3600:
                break

    if clear_seconds is None:
        clear_seconds = total_seconds
    return build, runs, clear_seconds, milestones, phases, full_hours


def zone_label(idx):
    return "보스 처치" if idx < 0 else ZONES[idx][0]


def report(seed=7, post_hours=40.0):
    build, runs, clear_seconds, milestones, phases, full_h = play_through(seed, post_hours)

    print(f"{'도달':<12}{'구간 런':>8}{'구간 분':>9}{'누적 h':>9}")
    for (m, r, h), (_, pr, pm) in zip(milestones, phases):
        print(f"{zone_label(m):<12}{pr:>8}{pm:>9.1f}{h:>9.2f}")
    print()
    clear_nodes = next((r for m, r, h in milestones if m < 0), None)
    print(f"클리어        : {clear_seconds/3600:.2f} 시간")
    print(f"풀트리 완성   : {('%.2f 시간 (클리어 후 +%.2f)' % (full_h, full_h - clear_seconds/3600)) if full_h else '못 채움'}")
    print(f"총 런 수      : {runs}")
    print(f"찍은 노드     : {build.nodes_bought}/{SLOT_COUNT}개")
    print(f"최종 기본크기 : {build.size:.1f}  (성장 상한 {build.size*GROWTH_MAX_MULT:.1f} / 보스 {BOSS_SIZE})"
          f"  장갑 {build.armor_levels}개")
    print(f"히든 아이템   : {build.hidden}/{HIDDEN_TOTAL}")
    print()
    for tid, kind, v, cm in TYPES:
        print(f"  {tid:<16}{build.lv[tid]:>4}/{MAX_LEVEL[tid]:<5}")


def clear_snapshot(seed=7):
    """클리어 시점에 몇 개를 찍었고 무엇을 찍었나."""
    build, runs, clear_seconds, ms, _, _ = play_through(seed, 0.0)
    print(f"클리어 {clear_seconds/3600:.2f}h · 노드 {build.nodes_bought}/{SLOT_COUNT} · 장갑 {build.armor_levels}/{MAX_LEVEL['armor']}")
    print("  " + "  ".join(f"{t[0]} {build.lv[t[0]]}/{MAX_LEVEL[t[0]]}" for t in TYPES))


def curve():
    print("N번째 노드 가격:")
    for n in [1, 2, 3, 5, 10, 20, 30, 50, 80, 100, 150, 200, 250, 300]:
        print(f"  {n:>4}번째  {cost_of_node(n):>18,.0f}   누적 {cumulative_cost(n):>20,.0f}")


def trials(n, post_hours=40.0):
    res, nodes, full = [], [], []
    for sd in range(n):
        b, runs, clear_s, ms, _, full_h = play_through(1000 + sd, post_hours)
        if ms and ms[-1][0] < 0:
            res.append(clear_s / 3600)
            nodes.append(sum(1 for m in ms if m[0] < 0) and b.nodes_bought)
            full.append(full_h - clear_s / 3600 if full_h else None)
    if not res:
        print("클리어 실패")
        return
    res.sort()
    print(f"시행 {n}회 — 완주 {len(res)}회")
    print(f"  클리어  최소 {res[0]:.2f}h / 중앙 {statistics.median(res):.2f}h / "
          f"최대 {res[-1]:.2f}h / 평균 {statistics.mean(res):.2f}h")
    ok = sorted(f for f in full if f is not None)
    if ok:
        print(f"  풀트리  클리어 후 +{statistics.median(ok):.2f}h (중앙, {len(ok)}/{len(full)}회 달성)"
              f"  최소 +{ok[0]:.2f}h / 최대 +{ok[-1]:.2f}h")
    else:
        print(f"  풀트리  {post_hours}h 안에 못 채움")


# ══════════════════════════════════════════════════════════════
#  스킬 전부 찍은 상태 점검 — `python3 balance_sim.py --full`
# ══════════════════════════════════════════════════════════════
def full_tree_build():
    b = Build()
    for i in range(len(SLOTS)):
        if i != ROOT:
            b.buy(i)
    b.hidden = HIDDEN_TOTAL
    b.deepest = len(ZONES) - 1
    for zi in range(len(ZONES)):
        for rank in range(len(SPAWN_WEIGHTS)):
            b.codex[(zi, rank)] = CODEX_MILESTONE * CODEX_MAX_TIERS
    return b


def full_tree(runs=300):
    b = full_tree_build()
    print("■ 스킬 전부 + 도감 전부 + 히든 8개")
    print(f"  총 노드 {b.nodes_bought}개")
    print(f"  기본 크기 {BASE_SIZE * 1.10 ** b.armor_levels:.1f} → 상한 적용 {b.size:.1f}   "
          f"(장갑 {b.armor_levels}레벨)")
    print("  판 중 최대 크기 (시작×2.5 / 절대 상한 / 구역 출구 한계 중 최소):")
    for zi, z in enumerate(ZONES):
        cap = max(b.size, min(b.size * GROWTH_MAX_MULT, zone_size_cap(zi)))
        fits = b.size <= zone_size_cap(zi)
        print(f"    {z[0]:<6} {cap:6.1f}   {'시작 가능' if fits else '시작 불가 (출구보다 몸이 큼)'}")
    print(f"  이동 속도 {b.speed:.1f}   입 배율 {b.mouth:.2f} (상한 {MAX_MOUTH_MULT})   "
          f"제한시간 {b.max_time:.0f}초")
    print(f"  재화 배율 ×{b.money_mult:.1f}   시간 회복 ×{b.time_mult:.2f}")
    vm = b.mult("volt_power")
    print(f"  볼트 마비 {min(2.0 * vm, 10 * 0.6):.1f}초 / 쿨 10초 (상한 6초)   "
          f"청소기 반경 = 몸 반경의 {5 * b.mult('vacuum_range') / 0.5:.0f}배")

    rng = random.Random(1)
    lens, boss, deaths, money = [], 0, 0, []
    for _ in range(runs):
        bb = full_tree_build()
        m, e, deepest, killed = simulate_run(bb, rng)
        lens.append(e); money.append(m)
        boss += killed
        if not killed and e < 25: deaths += 1
    lens.sort()
    print(f"\n  {runs}판 시뮬레이션 (바다 시작):")
    print(f"    한 판 길이 중앙 {statistics.median(lens):.0f}초 (최소 {lens[0]:.0f} / 최대 {lens[-1]:.0f})")
    print(f"    보스 처치율 {boss / runs * 100:.0f}%")


if __name__ == "__main__":
    if "--curve" in sys.argv:
        curve()
    elif "--full" in sys.argv:
        full_tree()
    elif "--clear" in sys.argv:
        clear_snapshot()
    elif "--trials" in sys.argv:
        trials(int(sys.argv[sys.argv.index("--trials") + 1]))
    else:
        report()
