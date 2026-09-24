"""
구간 페이싱을 잡으면서, 깊이에 따른 가치 상승을 보장한다.

손잡이
  ZONES[k][1]        구역 배율 — 구간별 소요 시간을 맞춘다 (순차 이분법)
  DEPTH_RICHNESS     맵 전체에 걸친 연속 깊이 곡선 (구역 경계에서 값이 안 튀게)

제약
  구역 k의 "물고기 한 마리당 실효 가치"는 반드시 앞 구역의 MIN_STEP배 이상이어야 한다.
  예전 튜닝은 이 제약이 없어서 어항→하수구가 1.26배로 거의 평평해졌다.
"""
import statistics
import balance_sim as B

TARGETS = [0.70, 1.60, 2.90, 4.50]   # 하수구 / 강 / 바다 도달, 보스 처치 (누적 h)
SEEDS = [11, 31, 53, 73, 97]
MIN_STEP = 2.0                       # 구역이 바뀔 때 실효 가치가 최소 몇 배 오르는가


def effective_value(k):
    """구역 k 한가운데에서 물고기 한 마리당 실효 재화."""
    return B.ZONES[k][4] * B.value_multiplier(k, 0.5)


def set_trim(k, v):
    z = list(B.ZONES[k]); z[1] = v; B.ZONES[k] = tuple(z)


def milestone_hours(k):
    vals = []
    for sd in SEEDS:
        _, runs, total, ms, _, _ = B.play_through(sd)
        if k < 3:
            if len(ms) > k and ms[k][0] >= 0:
                vals.append(ms[k][2])
            elif runs >= 20000:
                vals.append(99.0)
        else:
            vals.append(total / 3600 if (ms and ms[-1][0] < 0) else 99.0)
    return statistics.median(vals) if vals else None


def tune_zone(k, target, lo=1e-4, hi=3.0, iters=20):
    for _ in range(iters):
        mid = (lo + hi) * 0.5
        set_trim(k, mid)
        h = milestone_hours(k)
        if h is None or h > target:
            lo = mid            # 느리다 → 배율을 올린다
        else:
            hi = mid
    value = (lo + hi) * 0.5
    set_trim(k, value)

    # ── 단조 증가 보장 ──
    # 가치가 충분히 안 오르면 배율을 강제로 끌어올린다.
    # 그 구간은 목표보다 빨라지지만, "내려갈수록 값지다"가 깨지는 편이 더 나쁘다.
    note = ""
    if k > 0:
        need = effective_value(k - 1) * MIN_STEP
        if effective_value(k) < need:
            scale = need / effective_value(k)
            set_trim(k, B.ZONES[k][1] * scale)
            note = f"  ← 단조 보장을 위해 {scale:.2f}배 상향"

    h = milestone_hours(k)
    print(f"  {B.ZONES[k][0]:<8} 배율 {B.ZONES[k][1]:.4f}   "
          f"이정표 {h:.2f}h (목표 {target:.2f}h){note}")


def main():
    print(f"DEPTH_RICHNESS = {B.DEPTH_RICHNESS}   MIN_STEP = {MIN_STEP}배\n")
    print("구역 배율 순차 조정")
    for k, t in enumerate(TARGETS):
        tune_zone(k, t)

    print("\n확정값:")
    for k, z in enumerate(B.ZONES):
        print(f"  {z[0]:<8} {z[1]:.4f}f")

    print("\n물고기 한 마리당 실효 가치 (구역 한가운데):")
    prev = None
    for k, z in enumerate(B.ZONES):
        eff = effective_value(k)
        ratio = f"{eff/prev:.2f}배" if prev else "—"
        print(f"  {z[0]:<8} {eff:>10.4f}   앞 구역 대비 {ratio}")
        prev = eff

    print("\n구역 안에서의 상승 (천장 → 바닥):")
    for k, z in enumerate(B.ZONES):
        a, b = B.value_multiplier(k, 0.0), B.value_multiplier(k, 1.0)
        print(f"  {z[0]:<8} ×{a/B.ZONES[k][1]:.2f} → ×{b/B.ZONES[k][1]:.2f}")

    print("\n최종 검증 (25회):")
    B.trials(25)


if __name__ == "__main__":
    main()
