"""
물고기 인크리멘탈 게임 밸런스 시뮬레이터 (통합 맵 판).

맵 4개가 세로로 이어진 하나의 맵이 되었다.
한 판은 "도달해 본 가장 깊은 구역에서 시작해, 먹어서 커지면 통로가 열리고,
더 깊이 내려가 더 값진 물고기를 먹는다"로 바뀌었다.
목표: 총 플레이타임 4~5시간 안에 바다의 보스까지.

코스트는 노드별이 아니라 "지금까지 찍은 총 노드 수"로 정해진다.
    N번째 노드 = round(1.12^(N-1))   (정체 구간은 +1 보정)

python3 balance_sim.py              → 상세 리포트
python3 balance_sim.py --trials 60  → 60회 반복 분포
python3 balance_sim.py --curve      → 코스트 곡선 확인
"""
import math, random, sys, statistics

# ══════════════════════════════════════════════════════════════
#  코스트 곡선 (SkillNode.cs의 SkillCostCurve와 동일해야 함)
# ══════════════════════════════════════════════════════════════
GROWTH = 1.12
_COST_CACHE = []


def cost_of_node(n):
    """N번째(1부터)로 찍는 노드의 가격."""
    global _COST_CACHE
    if not _COST_CACHE:
        prev = 0.0
        cache = [0.0]
        for i in range(1, 1201):
            cur = float(round(GROWTH ** (i - 1)))
            if cur <= prev:
                cur = prev + 1.0
            cache.append(cur)
            prev = cur
        _COST_CACHE = cache
    if n < 1:
        n = 1
    return _COST_CACHE[n] if n < len(_COST_CACHE) else float(round(GROWTH ** (n - 1)))


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
#  노드 정의 : (id, 종류, 레벨당 값, 최대 레벨, 코스트 배율, 선행)
#     mult = 복리(×(1+v)), add = 가산
# ══════════════════════════════════════════════════════════════
NODES = [
    # (id, 종류, 레벨당 값, 최대 레벨, 코스트 배율, 선행 [(id, 필요레벨), ...])
    # --- 액티브 해금 ---
    ("unlock_booster",  "unlock", 1, 1,  3.0, []),
    ("unlock_vacuum",   "unlock", 1, 1,  5.0, []),
    ("unlock_armor",    "unlock", 1, 1,  7.0, []),
    ("unlock_bait",     "unlock", 1, 1,  9.0, []),
    ("unlock_volt",     "unlock", 1, 1, 12.0, []),
    ("unlock_missile",  "unlock", 1, 1, 16.0, []),

    # --- 기본 강화 ---
    ("battery",  "add",  1.00, 20, 1.0, []),   # 커다란 배터리 : 제한시간 +1초
    ("teeth",    "mult", 0.10,  8, 1.2, []),   # 치아 교정     : 입·흡입력 +10%
    ("camera",   "mult", 0.10,  8, 1.0, []),   # 카메라 장착   : 시야 +10%
    ("cell",     "mult", 0.05, 12, 1.0, []),   # 물고기 전지   : 시간 회복 +5%
    ("acid",     "mult", 0.10, 20, 1.5, []),   # 위액 산성도   : 재화 +10%

    # --- 덧붙인 장갑 (크기·속도 +10%) : 진행의 축. 4단계로 나눠 트리를 넓힌다 ---
    ("armor_1", "mult", 0.10, 12,  3.0, []),
    ("armor_2", "mult", 0.10, 10,  5.0, [("battery", 8), ("teeth", 5)]),
    ("armor_3", "mult", 0.10, 10,  8.0, [("acid", 8), ("cell", 5), ("unlock_vacuum", 1)]),
    ("armor_4", "mult", 0.10,  4, 12.0, [("camera", 5), ("teeth", 8),
                                         ("unlock_volt", 1), ("unlock_missile", 1)]),

    # --- 액티브 강화 ---
    ("booster_range", "mult", 0.30,  5, 1.0, [("unlock_booster", 1)]),
    ("booster_power", "unlock", 1,   1, 14.0, [("unlock_booster", 1)]),
    ("vacuum_range",  "mult", 0.50,  5, 1.0, [("unlock_vacuum", 1)]),
    ("armor_stack",   "add",  1.00,  2,  6.0, [("unlock_armor", 1)]),
    ("bait_range",    "mult", 0.50,  4, 1.0, [("unlock_bait", 1)]),
    ("bait_count",    "add",  1.00,  3,  5.0, [("unlock_bait", 1)]),
    ("volt_power",    "mult", 0.50,  5, 1.0, [("unlock_volt", 1)]),
    ("missile_power", "mult", 0.50,  5, 3.0, [("unlock_missile", 1)]),
]
ARMOR_TIERS = ["armor_1", "armor_2", "armor_3", "armor_4"]

NODE_BY_ID = {n[0]: n for n in NODES}

# 그리디 구매 가중치 — 실제 플레이어의 대략적인 선호도
WEIGHT = {
    "acid": 340, "battery": 26, "teeth": 240, "cell": 170, "camera": 70,
    "booster_range": 60, "vacuum_range": 70, "bait_range": 50, "volt_power": 60,
    "missile_power": 120, "armor_stack": 80, "bait_count": 60,
}
UNLOCK_PRIORITY = {
    "unlock_booster": 900, "unlock_vacuum": 1400, "unlock_armor": 1100,
    "unlock_bait": 900, "unlock_volt": 800, "unlock_missile": 2000,
    "booster_power": 700,
}

# ══════════════════════════════════════════════════════════════
#  맵 : (이름, 재화배율, 보스게이트, 필드 평균 size, 평균 재화, 평균 시간보상)
# ══════════════════════════════════════════════════════════════
# 구역: (이름, 보조배율, 통로필요크기, 필드평균size, 평균재화, 평균시간, 최소어size, 세로길이, 통로길이)
# 평균값은 ContentGenerator의 스폰 테이블(가중치 34/26/20/13/7 + 희귀어 2.5)의 가중평균이다.
# 구역 배율은 구간 페이싱을 잡는 손잡이다(tune.py가 정한다).
# 깊이에 따른 매끄러운 상승은 아래 DEPTH_RICHNESS 곡선이 따로 맡는다.
ZONES = [
    ("어항",   0.1474,  3.1,  0.877,  0.385, 1.029, 0.30,  46.0, 16.0),
    ("하수구", 0.0655,  8.1,  2.866,  1.566, 1.234, 1.00, 110.0, 22.0),
    ("강",     0.0271, 21.0,  7.676,  6.528, 1.537, 2.70, 260.0, 40.0),
    ("바다",   0.0124,  0.0, 17.800, 19.640, 1.740, 6.50, 520.0,  0.0),
]

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
        self.lv = {n[0]: 0 for n in NODES}
        self.nodes_bought = 0
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
        total = 0.0
        for (map_idx, rank), eaten in self.codex.items():
            btype, value = CODEX_BY_RANK[rank]
            if btype != kind:
                continue
            tiers = min(int(eaten // CODEX_MILESTONE), CODEX_MAX_TIERS)
            total += value * tiers
        return total

    # ── 스탯 ───────────────────────────────────────────────
    def mult(self, nid):
        _, kind, v, mx, cm, pre = NODE_BY_ID[nid]
        if nid in LINEAR_NODES:
            return 1.0 + v * self.lv[nid]
        return (1.0 + v) ** self.lv[nid]

    def has(self, nid):
        return self.lv[nid] > 0

    @property
    def armor_levels(self): return sum(self.lv[a] for a in ARMOR_TIERS)
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

    def can_buy(self, nid):
        _, kind, v, mx, cm, pre = NODE_BY_ID[nid]
        if self.lv[nid] >= mx:
            return False
        for pid, plv in pre:
            if self.lv[pid] < plv:
                return False
        return True

    def armor_goal(self):
        """
        크기를 올리기 위해 '지금 사야 할 노드'.
        상위 장갑 티어가 선행 때문에 막혀 있으면 그 선행 노드를 먼저 돌려준다.
        실제 플레이어가 트리를 뚫는 순서와 같다.
        """
        for a in ARMOR_TIERS:
            _, _, _, mx, _, pre = NODE_BY_ID[a]
            if self.lv[a] >= mx:
                continue
            for pid, plv in pre:
                if self.lv[pid] < plv:
                    _, _, _, pmx, _, _ = NODE_BY_ID[pid]
                    if self.lv[pid] < pmx:
                        return pid
            return a
        return None

    def price(self, nid):
        cm = NODE_BY_ID[nid][4]
        return math.ceil(cost_of_node(self.nodes_bought + 1) * cm)


def eat_interval(build, fish_size, size=None):
    """평균 포식 간격(초). size를 주면 인런 성장한 크기를 쓴다."""
    if size is None:
        size = build.size
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
            build.add_codex_kills(zone, kills_in_zone)
            zone += 1
            deepest = max(deepest, zone)
            time_in_zone = 0.0
            kills_in_zone = 0.0
            continue

        # ── 보스 ──
        # 바다에서 보스 크기를 넘기면 구조물을 깨고 삼킨다. 클리어.
        if zone == len(ZONES) - 1 and size >= BOSS_SIZE:
            money += BOSS_MONEY * value_multiplier(zone, 1.0) * build.money_mult
            build.add_codex_kills(zone, kills_in_zone)
            return money, elapsed + 12.0, deepest, True

        itv = eat_interval(build, fish_size, size)
        mps = missile_kills_per_second(build, fish_size, size)

        drain = min(MAX_DRAIN, 1.0 + (elapsed / 60.0) * DRAIN_ACCEL_PER_60S)
        t -= itv * drain
        elapsed += itv
        time_in_zone += itv
        if t <= 0:
            break

        # 위험 — 내가 클수록, 비늘이 남아 있을수록 안전.
        # 갓 내려온 구역에서는 주변이 전부 나보다 커서 훨씬 위험하다.
        danger = max(0.0, min(0.05, 0.016 * (fish_size * 2.2 - size) / max(1.0, fish_size)))
        if rng.random() < danger * itv:
            if armor > 0:
                armor -= 1
            else:
                build.add_codex_kills(zone, kills_in_zone)
                return money * (1 - DEATH_PENALTY), elapsed, deepest, False

        # 깊이가 값을 정한다 — 맵 전체에 걸친 연속 곡선
        depth01 = min(1.0, time_in_zone / DEPTH_RAMP_SECONDS)
        depth_mult = value_multiplier(zone, depth01)

        # 몸이 커질수록 그 구역의 더 큰 종을 먹게 되므로 마리당 재화가 오른다 (최대 3배)
        prey_bonus = min(3.0, max(0.4, size / fish_size))
        kills = 1.0 + mps * itv
        kills_in_zone += kills

        money += fish_money * build.money_mult * prey_bonus * depth_mult * kills
        t = min(build.max_time, t + fish_time * build.time_mult * kills)

        # ── 성장 ──
        if GROWTH_ENABLED:
            eaten_size = min(fish_size, size)      # 실제로 먹을 수 있는 크기 근사
            # 판 중 성장 상한 = min(시작×2.5, 절대 상한, 지금 구역 출구 한계)
            max_size = max(build.size, min(run_cap, zone_size_cap(zone)))
            if size < max_size:
                size = min(max_size, (size * size + eaten_size * eaten_size * GROWTH_EFFICIENCY) ** 0.5)

    build.add_codex_kills(zone, kills_in_zone)
    return money, elapsed, deepest, False


def greedy_buy(build, currency):
    """
    실제 플레이어의 구매 패턴 근사.
    "다음 크기 강화"를 목표로 두고, 그게 선행에 막혀 있으면 선행부터 뚫는다.
    목표 노드를 살 돈을 모으는 동안 남는 여윳돈으로만 나머지를 채운다.
    """
    guard = 0
    while guard < 5000:
        guard += 1

        goal = build.armor_goal()
        goal_price = build.price(goal) if goal else None

        # 1) 목표 노드를 살 수 있으면 산다
        if goal and currency >= goal_price:
            build.lv[goal] += 1
            build.nodes_bought += 1
            currency -= goal_price
            continue

        # 2) 목표를 살 돈이 한참 모자라면 그냥 모은다 (잡템을 사면 다음 노드 값이 올라 목표가 멀어진다)
        if goal and currency < goal_price * 3.0:
            return currency

        spare = currency if goal is None else currency - goal_price
        best, best_score, best_price = None, 0.0, 0
        for nid, kind, v, mx, cm, pre in NODES:
            if nid == goal or nid in ARMOR_TIERS or not build.can_buy(nid):
                continue
            p = build.price(nid)
            if p > spare:
                continue
            score = (UNLOCK_PRIORITY[nid] / p) if kind == "unlock" else (v * WEIGHT[nid] / p)
            if score > best_score:
                best, best_score, best_price = nid, score, p

        if best is None:
            return currency

        build.lv[best] += 1
        build.nodes_bought += 1
        currency -= best_price

    return currency


def play_through(seed):
    """
    게임 한 판이 아니라 '게임 전체'를 돌린다.
    진행의 기준은 이제 보스 클리어가 아니라 '새 구역 도달'이다.
    """
    rng = random.Random(seed)
    build = Build()
    currency, total_seconds, runs = 0.0, 0.0, 0

    milestones = []                 # (구역, 누적 런, 누적 시간h)
    phases = []                     # (구역, 구간 런, 구간 분)
    phase_start = (0, 0.0)
    cleared = False

    while not cleared and runs < 8000:
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

        if boss:
            cleared = True
            milestones.append((-1, runs, total_seconds / 3600))
            phases.append((-1, runs - phase_start[0], (total_seconds - phase_start[1]) / 60))
            break

        currency = greedy_buy(build, currency)

    return build, runs, total_seconds, milestones, phases


def zone_label(idx):
    return "보스 처치" if idx < 0 else ZONES[idx][0]


def report(seed=7):
    build, runs, total_seconds, milestones, phases = play_through(seed)

    print(f"{'도달':<12}{'구간 런':>8}{'구간 분':>9}{'누적 h':>9}")
    for (m, r, h), (_, pr, pm) in zip(milestones, phases):
        print(f"{zone_label(m):<12}{pr:>8}{pm:>9.1f}{h:>9.2f}")
    print()
    print(f"총 런 수      : {runs}")
    print(f"총 플레이타임 : {total_seconds/3600:.2f} 시간")
    print(f"평균 런 길이  : {total_seconds/max(1,runs) - MENU_SECONDS_PER_RUN:.0f}초")
    print(f"찍은 노드     : {build.nodes_bought}개")
    print(f"최종 기본크기 : {build.size:.1f}  (성장 상한 {build.size*GROWTH_MAX_MULT:.1f} / 보스 {BOSS_SIZE})"
          f"  장갑 {build.armor_levels}레벨")
    print(f"히든 아이템   : {build.hidden}/{HIDDEN_TOTAL}  (재화 +{build.hidden*HIDDEN_BONUS*100:.0f}%)")
    print(f"최종 속도     : {build.speed:.1f}")
    print(f"최종 제한시간 : {build.max_time:.0f}초")
    print()
    print(f"{'노드':<16}{'레벨':>10}")
    for nid, kind, v, mx, cm, pre in NODES:
        print(f"  {nid:<16}{build.lv[nid]:>4}/{mx:<5}")


def curve():
    print("N번째 노드 가격:")
    for n in [1, 2, 3, 5, 10, 20, 30, 50, 80, 100, 150, 200, 250, 300]:
        print(f"  {n:>4}번째  {cost_of_node(n):>18,.0f}   누적 {cumulative_cost(n):>20,.0f}")


def trials(n):
    res, nodes = [], []
    for s in range(n):
        b, runs, total, ms, _ = play_through(1000 + s)
        if ms and ms[-1][0] < 0:
            res.append(total / 3600)
            nodes.append(b.nodes_bought)
    if not res:
        print("클리어 실패")
        return
    res.sort()
    print(f"시행 {n}회 — 완주 {len(res)}회")
    print(f"  플레이타임  최소 {res[0]:.2f}h / 중앙 {statistics.median(res):.2f}h / "
          f"최대 {res[-1]:.2f}h / 평균 {statistics.mean(res):.2f}h")
    print(f"  찍은 노드   중앙 {statistics.median(nodes):.0f}개")


# ══════════════════════════════════════════════════════════════
#  스킬 전부 찍은 상태 점검 — `python3 balance_sim.py --full`
# ══════════════════════════════════════════════════════════════
def full_tree_build():
    b = Build()
    for nid, kind, v, mx, cm, pre in NODES:
        b.lv[nid] = mx
    b.nodes_bought = sum(n[3] for n in NODES)
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


def post_clear(seed=7, hour_cap=60):
    """클리어 후에도 계속 해서 트리를 다 채우려면 얼마나 걸리나."""
    build, runs, total, ms, _ = play_through(seed)
    rng = random.Random(seed + 1)
    currency = 0.0
    all_nodes = sum(n[3] for n in NODES)
    clear_h, clear_nodes = total / 3600, build.nodes_bought
    marks = {}
    while build.nodes_bought < all_nodes and total / 3600 < clear_h + hour_cap:
        m, e, _, _ = simulate_run(build, rng)
        currency += m
        total += e + MENU_SECONDS_PER_RUN
        # 남는 노드는 가장 싼 것부터
        while True:
            opts = [(build.price(n[0]), n[0]) for n in NODES if build.can_buy(n[0])]
            if not opts: break
            p, nid = min(opts)
            if p > currency: break
            currency -= p; build.lv[nid] += 1; build.nodes_bought += 1
        pct = build.nodes_bought * 100 // all_nodes
        for mark in (60, 70, 80, 90, 100):
            if pct >= mark and mark not in marks:
                marks[mark] = total / 3600 - clear_h
    print(f"\n■ 클리어 후 트리 채우기  (클리어 시점 {clear_nodes}/{all_nodes}노드, {clear_h:.2f}h)")
    for mark in (60, 70, 80, 90, 100):
        v = marks.get(mark)
        print(f"    {mark:>3}%  {'+%.1fh' % v if v is not None else f'{hour_cap}h 안에 도달 못 함'}")
    print(f"    다음 노드 가격 {build.price(min((n for n in NODES if build.can_buy(n[0])), key=lambda n: build.price(n[0]))[0]) if any(build.can_buy(n[0]) for n in NODES) else 0:,}")


if __name__ == "__main__":
    if "--curve" in sys.argv:
        curve()
    elif "--full" in sys.argv:
        full_tree()
        post_clear()
    elif "--trials" in sys.argv:
        trials(int(sys.argv[sys.argv.index("--trials") + 1]))
    else:
        report()
