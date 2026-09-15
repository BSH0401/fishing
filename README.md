# 물고기 인크리멘탈 게임 — Unity 6 프로토타입

기획서 2판(`인크리멘탈_물고기게임 2.docx`) 기반. 실험체 물고기가 실험실에서 탈출해 바다로 향하는
2D 횡스크롤 인크리멘탈 게임의 **플레이 가능한 전체 프로토타입**입니다.

```
메인 화면 (스킬트리 · 맵 선택) → 플레이 → 결과 → 메인 화면
```

- **엔진**: Unity 6 (6000.x) / 신 Input System
- **기획서**: 2판 (액티브 스킬 6종 + 강화 14종)
- **데이터**: ScriptableObject + JSON 세이브 (`Application.persistentDataPath`)
- **목표 플레이타임**: 4~5시간 → 시뮬레이션 25회 검증 결과 **4.41 ~ 4.47h (중앙 4.44h)**

---

## 1. 5분 만에 실행하기

### 1) 프로젝트 만들기

Unity Hub → New Project → **2D (Built-in Render Pipeline)** 또는 **2D (URP)** → Unity 6 (6000.x)

### 2) 패키지 확인

`Window ▸ Package Manager` 에서 아래 두 개가 설치돼 있어야 합니다. (Unity 6 2D 템플릿엔 보통 기본 포함)

| 패키지 | 용도 |
|---|---|
| **Input System** (`com.unity.inputsystem`) | 조작 |
| **TextMeshPro** (Unity 6에서는 `com.unity.ugui`에 포함) | UI 텍스트 |

Input System을 새로 설치했다면 `Project Settings ▸ Player ▸ Active Input Handling`을
**Input System Package (New)** 로 바꾸고 에디터를 재시작하세요.

### 3) TMP 리소스 임포트 (필수)

`Window ▸ TextMeshPro ▸ Import TMP Essential Resources`

> **한글 주의**: TMP 기본 폰트(LiberationSans)에는 한글 글리프가 없어 □ 로 보입니다.
> 5번 항목의 한글 폰트 설정을 꼭 하세요.

### 4) 스크립트 복사

`Assets/_Project/` 폴더를 통째로 프로젝트의 `Assets/` 아래에 넣습니다.

```
Assets/_Project/
├── Editor/          ← 에디터 전용 (빌드에 포함되지 않음)
├── Scripts/
│   ├── Core/        GameManager, SaveSystem, PlayerProgress, PlayerStats, SkillTreeManager
│   ├── Data/        FishSpecies, MapData, SkillNode, GameDatabase (ScriptableObject)
│   ├── Gameplay/    RunManager, FishBody, FishMotor, AIFish, FishSpawner, Bait, Missile, CameraFollow
│   ├── Player/      PlayerFish, FishInput
│   ├── UI/          HUD, 스킬트리, 결과, 일시정지, 맵 선택
│   └── Utils/       NumberFormatter
├── Prefabs/         (자동 생성)
├── ScriptableObjects/ (자동 생성)
├── Art/Generated/   (자동 생성 — 플레이스홀더 스프라이트)
└── Scenes/          (자동 생성)
```

### 5) 원클릭 셋업

컴파일이 끝나면 상단 메뉴에 **FishGame** 이 생깁니다.

**`FishGame ▸ ★ 전체 셋업 (원클릭)`**

이 한 번으로 아래가 전부 만들어집니다.

| 단계 | 내용 |
|---|---|
| 0 | `Fish` / `Player` 레이어 추가, 2D 중력 0, 레이어 충돌 해제 |
| 1 | 물고기 24종 · 맵 4개 · 스킬 23개 · GameDatabase + 플레이스홀더 스프라이트 |
| 2 | PlayerFish / AIFish / 미끼 / 미사일 / UI 프리팹 |
| 3 | MainMenu · Gameplay 씬 + Build Settings 등록 |

끝나면 **MainMenu 씬이 열립니다. ▶ Play 를 누르면 바로 플레이됩니다.**

### 6) 한글 폰트 (권장)

1. 한글 폰트(예: `NotoSansKR-Regular.ttf`, Pretendard)를 `Assets/_Project/Art/Fonts/` 에 넣기
2. `Window ▸ TextMeshPro ▸ Font Asset Creator`
3. Source Font = 그 폰트 / **Character Set = `Unicode Range (Hex)`** /
   Range에 `20-7E,AC00-D7A3,3131-318E` 입력 / Atlas 4096×4096, Render Mode SDFAA
4. Generate → Save 로 `.asset` 저장
5. `Project Settings ▸ TextMesh Pro ▸ Settings ▸ Default Font Asset` 을 이 에셋으로 교체
6. 이미 만든 씬은 `FishGame ▸ 3. 씬 생성` 을 다시 눌러 재생성하면 새 폰트가 적용됩니다

---

## 2. 조작

| 입력 | 동작 |
|---|---|
| `WASD` / 방향키 / 좌스틱 | 이동 |
| **우클릭** (또는 `Space`) | **부스터** — 앞으로 대쉬, 경로의 물고기를 크기 무시하고 먹음 |
| **좌클릭 홀드** (또는 `E`) | **청소기** — 범위 내 소형 물고기를 끌어당김 |
| `ESC` | 일시정지 |

**황금 미끼 · 10만 볼트 · 미사일**은 해금하면 자동으로 발동합니다.
**비늘 경화**는 패시브로, 먹힐 뻔한 순간을 막아줍니다.

마우스로 물고기를 끌고 다니는 조작을 원하면
PlayerFish 인스펙터의 **Control Scheme** 을 `Mouse Follow` 로 바꾸세요.

## 3. 시스템 구조

```
GameManager (DontDestroyOnLoad, 씬을 넘어 유지)
├── GameDatabase (SO)  ── maps[] / skills[] / 기본 스탯 / 규칙
├── PlayerProgress     ── 재화, 스킬 레벨, 해금 맵, 통계  → JSON 세이브
└── PlayerStats        ── 판 시작 시 스킬 레벨을 합산한 최종 스탯

[MainMenu 씬]
  SkillTreeUI  ─ SkillTreeManager.Purchase() ─→ PlayerProgress
  MapSelectUI  ─ GameManager.SelectMap()
  MainMenuUI   ─ GameManager.StartRun()

[Gameplay 씬]
  RunManager (한 판의 소유자)
  ├── 제한시간 / 획득 재화 / 보스 게이트
  ├── PlayerFish  ─ 이동 · 포식 판정 · 액티브 스킬
  ├── FishSpawner ─ 오브젝트 풀 + 가중치 스폰
  └── 종료 → GameManager.FinishRun() → 정산 → ResultPanel
```

### 포식 판정이 일어나는 곳은 한 군데뿐입니다

`PlayerFish.ResolveFishContacts()` 가 매 FixedUpdate에 주변을 `OverlapCircle` 로 훑고

- **내 `Size` × `eatSizeTolerance` ≥ 상대 `Size`** 이고 **입(mouth) 판정원에 닿으면** → 내가 먹음
- **상대가 나를 먹을 수 있고 몸통이 겹치면** → 내가 죽음

AI 쪽은 판정에 관여하지 않습니다. 양쪽에서 처리하면 같은 프레임에 서로를 먹는 버그가 생기기 때문입니다.

### 유영 모션 — `FishMotor`

플레이어와 AI가 같은 컴포넌트를 씁니다. 물속처럼 보이게 하는 건 세 가지입니다.

1. **가속과 감속을 따로** — 멈출 땐 지수 감쇠로 미끄러지듯 느려집니다
2. **선회 속도 제한** — 방향을 꺾어도 즉시 돌지 않고 호를 그립니다. 크고 둔한 물고기일수록 `turnRate` 가 낮습니다
3. **진행 방향으로 회전 + 꼬리 흔들림** — 좌우 반전만 하면 뻣뻣해 보입니다. 몸을 이동 방향으로 돌리고, 속도에 비례해 꼬리를 흔듭니다. 왼쪽을 볼 땐 `flipY` 로 배가 아래로 오게 합니다

AI 쪽에는 스티어링을 얹었습니다 — 이웃과 서로 밀어내기(separation), 벽에 닿기 전에 미리 방향 틀기,
개체마다 ±18% 속도 차이, 목표에 가까워지면 감속. 격자처럼 움직이던 게 사라집니다.

추격형은 목표의 **조금 앞**을 노려서 뒤꽁무니만 쫓지 않습니다.

### 스폰은 맵 전체가 아니라 플레이어 주변에

맵이 커지면(바다는 620×340) 균일 스폰으로는 화면이 텅 빕니다.
`FishSpawner` 는 카메라 시야 반경을 기준으로 **화면 바로 바깥 링**에 스폰하고,
시야 2.8배를 벗어난 물고기는 회수해 다시 씁니다. 보스는 회수하지 않습니다.

### 타격감 — `Juice`

먹는 순간 세 가지가 동시에 일어납니다. 이게 없으면 아무리 시스템이 많아도 프로토타입처럼 느껴집니다.

1. **히트스톱** — 화면이 0.012~0.075초 멈춥니다. 큰 걸 먹을수록 오래. 씹는 맛의 대부분이 여기서 나옵니다
2. **화면 흔들림** — 트라우마 기반(제곱 감쇠)이라 잔챙이는 티가 안 나고 큰 것만 확 흔들립니다
3. **크기 팝** — 몸이 6~30% 부풀었다 돌아옵니다

히트스톱은 `Time.timeScale = 0` 이므로, 관련 연출은 전부 `unscaledTime` 을 씁니다
(파티클 `useUnscaledTime`, 카메라 `SmoothDamp`, 팝 곡선). ESC 일시정지와 겹쳐도 안 풀리게 막아뒀습니다.

제한시간이 6초 이하로 떨어지면 화면 가장자리가 붉게 맥동하고, 남을수록 빨라집니다.

### 사운드 — 클립 없이도 돌아갑니다

`Assets/_Project/Resources/SoundBank.asset` 에 슬롯 15개가 비어 있습니다.
`.wav`를 끌어다 넣기만 하면 소리가 나고, 비어 있으면 조용히 넘어갑니다.
같은 클립이 0.04초 안에 겹치면 무시하고, 음정을 ±12% 흔들어 단조로움을 없앱니다.

### 인게임 성장 — 먹을수록 커진다

한 판 안에서 먹은 만큼 몸이 커집니다. 면적(질량)을 더하는 방식입니다.

```
새 크기 = √(내 크기² + 먹이 크기² × 0.15)
```

곱셈이 아니라 면적을 더하기 때문에 **커질수록 잔챙이로는 거의 안 크고, 큰 걸 먹으면 확 큽니다.**
감속이 저절로 생겨서 별도 보정이 필요 없습니다. 한 판에서 최대 **시작 크기의 2.5배**까지.

커지면 입 판정·청소기·볼트 범위가 다 같이 커지고, 카메라도 따라 줌아웃됩니다.
HUD 좌상단 주황색 바가 성장 진행도입니다.

**보스 게이트는 여전히 스킬트리로 올린 기본 크기로만 판정합니다.**
인런 성장으로 게이트를 넘게 두면 스킬트리를 찍을 이유가 사라지기 때문입니다.

성장 때문에 한 판이 137초까지 길어져서, 드레인 가속을 1.1 → **2.2**로 올려 75초로 되돌렸습니다.

### 스킬트리 읽는 법

선행선이 3단계 색으로 나뉩니다. 선 위의 `3/8` 라벨이 남은 요구 레벨입니다.

| 색 | 의미 |
|---|---|
| 흐린 회색 | 선행을 아직 안 찍음 |
| 노랑 | 찍었지만 요구 레벨 미달 (`3/8`) |
| 초록 (굵게) | 조건 충족 |

노드는 계열별로 색이 다릅니다 — <b>크기</b> 초록 / <b>시간</b> 파랑 / <b>재화</b> 금색 / <b>유틸</b> 보라 / <b>액티브</b> 주황.
지금 살 수 있는 노드만 은은하게 맥동하고, 노드 안쪽이 레벨만큼 아래에서 위로 차오릅니다.
노드에 마우스를 올리면 우측 패널에 **부족한 선행 조건이 ✓/✗ 목록**으로 나옵니다.

조작은 **휠 = 확대·축소 (커서 위치 기준), 드래그 = 이동**입니다.
좌상단 `−` `+` `전체 보기` 버튼으로도 됩니다. 처음 열면 트리 전체가 화면에 들어오게 자동으로 맞춰집니다.
트리 영역(Content)은 노드 좌표 범위를 실제로 재서 잡으므로, 스킬을 추가해도 잘리지 않습니다.

### 물고기 도감

종별로 먹은 수가 누적되고, 100마리마다 **그 종의 고유 효과**가 한 단계씩 붙습니다.
맵 안에서 크기 순서대로 배정돼 있습니다.

| 크기 순위 | 효과 | 단계당 |
|---|---|---|
| 1 (가장 흔한 잡어) | 재화 | +2% |
| 2 | 시간 회복 | +2% |
| 3 | 입·흡입력 | +1.8% |
| 4 | 이동 속도 | +1.5% |
| 5 (가장 큰 종) | 제한시간 | +0.6초 |

최대 10단계까지. 보스는 100마리를 먹을 일이 없으므로 수집 항목으로만 표시됩니다.
`PlayerProgress.codex` 에 저장되며 죽어도 사라지지 않습니다.
메인 화면의 **물고기 도감** 버튼으로 확인합니다.

### 제한시간 드레인 가속 — 기획서에 없던 추가 규칙

기획서대로 "먹으면 제한시간이 조금 늘어난다"만 구현하면, 강화가 쌓인 뒤에는
**포식 속도 > 시간 소모 속도**가 되어 한 판이 무한정 길어집니다. 실제로 시뮬레이션에서
후반 한 판이 470초까지 늘어나며 3레벨 루프가 무너졌습니다.

그래서 `GameDatabase.timeDrainAccelerationPer60s = 1.1` 을 넣었습니다.

```
초당 소모량 = 1 + (경과초 / 60) × 2.2     (최대 10배)
```

60초를 넘기면 3.2배, 120초면 5.4배로 압박이 붙어 판이 자연스럽게 끝납니다.
HUD 좌상단에 `소모 x2.1` 로 표시돼 플레이어도 압박을 눈으로 확인할 수 있습니다.
이 값을 0으로 두면 원래 기획대로 돌아가지만, 루프가 무너지는 점은 감안하세요.

### 보스 게이트는 "스킬트리로 올린 기본 크기"로 판정합니다

`GameDatabase.bossGateUsesBaseSize = true`.
한 판 안의 변동(부스터로 큰 물고기를 먹는 등)으로 게이트를 건너뛸 수 있게 두면
스킬트리를 찍지 않고도 진행돼 성장 루프가 무너집니다.
끄고 싶으면 인스펙터에서 체크만 해제하세요.

### 이동 속도는 크기의 제곱근으로 완화했습니다

기획서의 덧붙인 장갑은 "몸 크기 및 이동속도 10% 증가"입니다.
크기는 복리로 45배까지 커지는데 속도까지 45배가 되면 조작이 불가능해집니다.
`GameDatabase.speedScalingExponent = 0.5` 로 속도는 크기 배율의 제곱근만 따라갑니다
(45배 커지면 속도는 6.7배). `maxMoveSpeed = 24` 로 상한도 있습니다.
1로 두면 기획서 그대로가 되지만 후반이 조작 불가가 됩니다.

---

## 4. 밸런스

`Tools/balance_sim.py` 가 밸런스의 단일 출처입니다.
`ContentGenerator.cs` 의 수치는 이 파일과 1:1로 맞춰져 있습니다.

```bash
python3 Tools/balance_sim.py              # 상세 리포트
python3 Tools/balance_sim.py --trials 40  # 40회 분포
python3 Tools/balance_sim.py --curve      # 코스트 곡선
```

### 검증 결과 (25회 시행, 전부 완주 · 도감 보너스 + 인게임 성장 포함)

| 항목 | 값 |
|---|---|
| 총 플레이타임 | **4.41h ~ 4.47h** (중앙 4.44h) |
| 총 런 수 | 약 190회 |
| 평균 한 판 | 약 75초 |
| 찍는 노드 | 약 81개 |

| 맵 | 재화 배율 | 보스 게이트(크기) | 구간 런 | 구간 시간 |
|---|---|---|---|---|
| 1. 어항 | ×0.289 | 3.1 | 50 | 36분 |
| 2. 하수구 | ×0.210 | 8.1 | 47 | 51분 |
| 3. 강 | ×0.227 | 21.0 | 44 | 76분 |
| 4. 바다 | ×0.545 | 45.0 | 48 | 104분 |

### 코스트 곡선 — 기획서의 의사코드 그대로

```
N번째로 찍는 노드의 값 = round(1.12^(N-1))
반올림 때문에 값이 정체되면 강제로 previousCost + 1
```

**중요: 코스트는 노드별이 아니라 "지금까지 찍은 총 노드 수"로 정해집니다.**
어떤 노드를 먼저 찍든 N번째 노드의 가격은 같습니다.
노드마다 `costMultiplier` 로 무게만 다르게 줬습니다 (액티브 해금은 3~16배).

| N | 가격 | 누적 |
|---|---|---|
| 10 | 10 | 55 |
| 30 | 30 | 465 |
| 50 | 258 | 2,625 |
| 80 | 7,731 | 72,371 |
| 100 | 74,573 | 696,234 |

### 스킬 (액티브 6 + 강화 14 + 크기 4단계)

| 분류 | 노드 |
|---|---|
| **액티브 해금** | 부스터 · 청소기 · 비늘 경화 · 황금 미끼 · 10만 볼트 · 미사일 |
| **액티브 강화** | 부스터 거리/위력 · 청소기 범위 · 비늘 경화 강화 · 미끼 범위/추가 · 볼트 강화 · 미사일 발사 강화 |
| **기본 강화** | 커다란 배터리 · 치아 교정 · 카메라 장착 · 물고기 전지 · 위액 산성도 증가 |
| **진행의 축** | 덧붙인 장갑 I~IV (몸 크기·이동속도 +10% 복리) |

**덧붙인 장갑을 4단계로 나눈 이유**: 코스트가 전역 노드 수로 정해지므로,
장갑만 계속 찍으면 40노드 만에 게임이 끝나 버립니다.
각 단계에 다른 가지를 선행으로 걸어 트리가 자연히 넓어지게 했습니다.

| 단계 | 최대 | 선행 조건 |
|---|---|---|
| I | 12 | 없음 |
| II | 10 | 커다란 배터리 8 · 치아 교정 5 |
| III | 10 | 위액 산성도 8 · 물고기 전지 5 · 청소기 해금 |
| IV | 10 | 카메라 5 · 치아 교정 12 · 볼트 해금 · 미사일 해금 |

### 수치를 바꾸고 싶다면

1. `Tools/balance_sim.py` 의 `NODES` / `MAPS` 를 수정
2. `python3 Tools/balance_sim.py --trials 40` 으로 플레이타임 확인
3. 만족스러우면 `ContentGenerator.cs` 의 같은 항목에 반영
4. Unity에서 `FishGame ▸ 1. 콘텐츠 에셋 생성` 재실행

## 5. 콘텐츠 추가하기

### 물고기 1종 추가

`Create ▸ FishGame ▸ Fish Species` → 값 입력 → 해당 `MapData` 의 `Spawn Table` 에 추가.
프리팹은 만들 필요 없습니다. `AIFish` 프리팹 하나를 풀에서 재사용하며
스프라이트·크기·패턴을 `FishSpecies` 에서 주입합니다.

AI 패턴은 `AIPatternType` 에 정의된 6종입니다.

| 패턴 | 동작 | `patternParam` | `patternParam2` |
|---|---|---|---|
| `Straight` | 직진, 벽에서 반사 | – | – |
| `SineWave` | 사인파 유영 | 진폭 | 주기(Hz) |
| `Wander` | 랜덤 배회 | 배회 반경 | – |
| `Chase` | 자기보다 작으면 추격 | 감지 반경 | – |
| `Flee` | 자기보다 크면 도주 | 감지 반경 | – |
| `Ambush` | 정지 → 사거리 안에 들어오면 급습 | 감지 반경 | 급습 배속 |

### 스킬 1개 추가

`Create ▸ FishGame ▸ Skill Node` → `id`(고유, **변경 금지**), 효과, 비용, `gridPosition`,
`prerequisites` 설정 → `GameDatabase.skills` 에 추가.

`gridPosition` 은 UI 격자 좌표라 좌표만 정하면 스킬트리 UI가 자동 배치하고 연결선을 그립니다.
`id` 를 바꾸면 저장된 레벨이 유실됩니다.

### 맵 1개 추가

`Create ▸ FishGame ▸ Map Data` → `mapIndex` 를 연속으로, 스폰 테이블·보스·게이트 설정 →
`GameDatabase.maps` 에 추가.

---

## 6. 다음에 붙이면 좋은 것 (우선순위 순)

1. **아트 교체** — `Assets/_Project/Art/Generated/` 의 PNG를 같은 이름으로 덮어쓰면 끝
2. **사운드** — 포식/대쉬/사망/보스 등장. `RunManager.OnFishEaten` 등 이벤트가 이미 열려 있음
3. **연출** — 포식 시 화면 흔들림, 크기 성장 트윈, 물결 셰이더
4. **보스 패턴** — 지금은 일반 물고기와 같은 AI. `AIFish` 를 상속한 `BossFish` 로 페이즈 추가
5. **오프라인 보상 / 자동 사냥** — 인크리멘탈 장르의 리텐션 장치
6. **업적 · 통계 화면** — `PlayerProgress` 에 통계 필드가 이미 쌓이고 있음

---

## 7. 문제 해결

| 증상 | 원인 / 해결 |
|---|---|
| UI 텍스트가 안 보임 | TMP Essential Resources 미임포트 |
| 한글이 □ 로 보임 | 1-6의 한글 폰트 설정 필요 |
| 물고기를 먹을 수 없음 | `PlayerFish` 의 `Fish Layer` 마스크가 `Fish` 인지 확인 |
| 물고기가 안 나옴 | `MapData.spawnTable` 이 비었거나 `FishSpawner.fishPrefab` 미지정 |
| `InputSystemUIInputModule` 컴파일 오류 | Input System 패키지 미설치 |
| 진행도를 초기화하고 싶음 | 메인 화면의 `세이브 초기화` 버튼 또는 `FishGame ▸ 세이브 파일 삭제` |
| Gameplay 씬만 단독 실행하고 싶음 | `RunManager` 의 `Fallback Database` / `Fallback Map Index` 사용 (GameManager 없이 동작) |

세이브 위치: `FishGame ▸ 세이브 폴더 열기`
(Windows 기준 `%USERPROFILE%\AppData\LocalLow\<회사명>\<프로젝트명>\progress.json`)
