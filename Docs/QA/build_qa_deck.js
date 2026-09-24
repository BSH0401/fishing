// QA 리포트 PPT 생성기
//
//   node build_qa_deck.js            → 물고기게임_QA리포트.pptx
//
// 내용은 전부 qa_log.json에 있다. QA를 한 번 더 하면 rounds에 한 회차를 추가하고
// risks를 갱신한 뒤 이 스크립트를 다시 돌리면 된다. (슬라이드 수·페이지 번호는 자동)
//
// 디자인: 포트폴리오 디자인 시스템 — Pretendard 단일 서체, Action Blue(#0066CC) 단일 강조색,
// 16:9(13.333" × 7.5"), 챕터 태그·제목·구분선 고정 좌표.

const fs = require("fs");
const path = require("path");
const pptxgen = require("pptxgenjs");

const data = JSON.parse(fs.readFileSync(path.join(__dirname, "qa_log.json"), "utf8"));
const OUT = process.argv[2] || path.join(__dirname, "물고기게임_QA리포트.pptx");

// ── 디자인 토큰 ─────────────────────────────────────────────
const C = {
  primary: "0066CC", primaryOnDark: "2997FF",
  canvas: "FFFFFF", parchment: "F5F5F7", tile: "272729", tile2: "2A2A2C",
  ink: "1D1D1F", ink80: "333333", ink48: "7A7A7A",
  onDark: "FFFFFF", onDarkMuted: "CCCCCC",
  hairline: "E0E0E0", soft: "F0F0F0",
  blueMid: "7FB2E5", grayMid: "C7C7CC",
};
const F = {
  reg: "Pretendard", light: "Pretendard Light", med: "Pretendard Medium",
  semi: "Pretendard SemiBold", bold: "Pretendard Bold",
};
const X = 0.667, W = 12.0;
const SEV = { High: "높음", Med: "중간", Low: "낮음" };

const pres = new pptxgen();
pres.defineLayout({ name: "WIDE_16_9", width: 13.333, height: 7.5 });
pres.layout = "WIDE_16_9";
pres.title = `${data.project} QA 리포트`;

// ── 공통 요소 ───────────────────────────────────────────────
function text(slide, str, o) {
  slide.addText(str, Object.assign({ isTextBox: true, margin: 0, valign: "top", fontFace: F.reg, color: C.ink }, o));
}

function grid(slide, chapter, title, dark = false) {
  text(slide, chapter, {
    x: X, y: 0.35, w: W, h: 0.25, fontFace: F.semi, fontSize: 11, bold: true,
    color: dark ? C.primaryOnDark : C.primary, charSpacing: /[가-힣]/.test(chapter) ? 0 : 2,
  });
  text(slide, title, { x: X, y: 0.65, w: W, h: 0.85, fontFace: F.semi, fontSize: 40, color: dark ? C.onDark : C.ink });
  slide.addShape(pres.shapes.LINE, { x: X, y: 1.55, w: W, h: 0, line: { color: dark ? "3A3A3C" : C.hairline, width: 0.75 } });
}

function footer(slide, page, total, dark = false) {
  const col = dark ? "8E8E93" : C.ink48;
  text(slide, `QA REPORT  ·  ${data.projectEn}`, { x: X, y: 7.05, w: 6, h: 0.3, fontSize: 10, color: col, charSpacing: 2, valign: "middle" });
  text(slide, `${String(page).padStart(2, "0")} / ${String(total).padStart(2, "0")}`,
       { x: 11.5, y: 7.05, w: 1.166, h: 0.3, fontSize: 10, color: col, align: "right", valign: "middle" });
}

/** Pattern A — 셀 4개짜리 데이터 띠 */
function dataStrip(slide, y, cells, dark = false) {
  const n = cells.length, cw = W / n;
  cells.forEach((c, i) => {
    const cx = X + i * cw;
    if (i > 0) slide.addShape(pres.shapes.LINE, { x: cx, y: y + 0.05, w: 0, h: 0.95, line: { color: dark ? "3A3A3C" : C.hairline, width: 0.75 } });
    const pad = i === 0 ? 0 : 0.3;
    text(slide, c.value, { x: cx + pad, y, w: cw - pad - 0.1, h: 0.55, fontFace: F.semi, fontSize: 28, color: dark ? C.onDark : C.ink, valign: "middle" });
    text(slide, c.label, { x: cx + pad, y: y + 0.58, w: cw - pad - 0.1, h: 0.25, fontSize: 12, color: dark ? C.onDarkMuted : C.ink48 });
    if (c.sub) text(slide, c.sub, { x: cx + pad, y: y + 0.82, w: cw - pad - 0.1, h: 0.22, fontSize: 10.5, color: dark ? "8E8E93" : C.ink48 });
  });
}

function sevCount(items) {
  const c = { High: 0, Med: 0, Low: 0 };
  items.forEach((it) => (c[it.sev] = (c[it.sev] || 0) + 1));
  return c;
}

// ── 표 쪽 나누기 (행 높이 추정) ─────────────────────────────
const FS = 13;                           // 표 본문 글자 크기(pt)
const LINE_H = (FS * 1.38) / 72;         // 한 줄 높이(in)
function lines(str, colW) {
  // 한글 1자 ≈ 1em, 영문·숫자·공백 ≈ 0.55em
  let em = 0;
  for (const ch of String(str)) em += /[가-힣ㄱ-ㅎ→·×≥≤…]/.test(ch) ? 0.88 : 0.5;
  const perLine = ((colW - 0.2) * 72) / FS;
  return Math.max(1, Math.ceil(em / perLine));
}
const ROW_PAD = 0.2;
/** 행을 쪽에 나눠 담는다. 여러 쪽이면 쪽마다 비슷한 높이가 되게 고르게 나눈다. */
function paginate(rows, colW, maxH) {
  const sized = rows.map((r) => ({ cells: r, h: Math.max(...r.map((cell, i) => lines(cell, colW[i]))) * LINE_H + ROW_PAD }));
  const total = sized.reduce((a, r) => a + r.h, 0);
  const n = Math.max(1, Math.ceil(total / maxH));
  const target = total / n;
  const pages = [[]]; let used = 0;
  sized.forEach((r) => {
    const last = pages[pages.length - 1];
    if (last.length && (used + r.h > maxH || (pages.length < n && used + r.h / 2 > target))) { pages.push([]); used = 0; }
    pages[pages.length - 1].push(r); used += r.h;
  });
  return pages;
}
const pageHeight = (pg) => 0.36 + pg.reduce((a, r) => a + r.h, 0);

function table(slide, y, header, colW, pageRows, styleCell) {
  const border = (b) => [{ type: "none" }, { type: "none" }, b ? { type: "solid", pt: 0.75, color: C.hairline } : { type: "none" }, { type: "none" }];
  const head = header.map((h) => ({
    text: h, options: { fontFace: F.semi, fontSize: 12, bold: true, color: C.ink48, fill: { color: C.parchment }, border: border(true), valign: "middle", margin: [4, 6, 4, 6] },
  }));
  const body = pageRows.map((r) => r.cells.map((cell, i) => ({
    text: String(cell),
    options: Object.assign({ fontFace: F.reg, fontSize: FS, color: C.ink80, fill: { color: C.canvas }, border: border(true), valign: "middle", margin: [4, 6, 4, 6] }, styleCell(i, cell)),
  })));
  slide.addTable([head, ...body], {
    x: X, y, w: W, colW, rowH: [0.36, ...pageRows.map((r) => r.h)], autoPage: false,
  });
}

// ══════════════════════════════════════════════════════════
//  슬라이드 계획 — 먼저 목록을 만들고, 전체 쪽수를 안 뒤에 그린다
// ══════════════════════════════════════════════════════════
const plan = [];
const allItems = data.rounds.flatMap((r) => r.items);
const fixed = allItems.filter((i) => i.status === "수정").length;
const openRisks = data.risks.filter((r) => r.status !== "해결").length;

// 1. 표지
plan.push((s, p, t) => {
  s.background = { color: C.tile };
  text(s, "QA REPORT", { x: X, y: 2.05, w: W, h: 0.3, fontFace: F.semi, fontSize: 12, bold: true, color: C.primaryOnDark, charSpacing: 2 });
  text(s, `${data.project}\nQA 리포트`, { x: X, y: 2.45, w: W, h: 2.0, fontFace: F.bold, fontSize: 56, color: C.onDark, lineSpacingMultiple: 1.05 });
  text(s, "플레이테스트 · 코드 리뷰 · 밸런스 검증 기록", { x: X, y: 4.85, w: W, h: 0.45, fontFace: F.light, fontSize: 22, color: C.onDarkMuted });
  text(s, `업데이트 ${data.updated}   ·   QA ${data.rounds.length}회   ·   이슈 ${allItems.length}건`,
       { x: X, y: 5.55, w: W, h: 0.3, fontSize: 13, color: "8E8E93" });
});

// 2. 진행 방식
plan.push((s, p, t) => {
  s.background = { color: C.parchment };
  grid(s, "00  PROCESS", "QA는 이렇게 진행했습니다");
  const steps = [
    ["01", "제보 · 로그", "플레이테스트에서 나온 제보와 Unity Editor.log의 오류·경고를 먼저 모은다"],
    ["02", "영역별 코드 리뷰", "스킬트리 · 플레이 루프 · 데이터로 나눠 코드를 끝까지 읽고, 재현 경로가 분명한 것만 이슈로 올린다"],
    ["03", "시뮬레이터 대조", "밸런스 시뮬레이터의 식과 실제 게임 코드를 한 줄씩 맞대어 어긋나는 가정을 찾는다"],
    ["04", "수정 · 재검증", "최소 수정 후 정적 검사와 25회 시뮬레이션으로 다시 확인하고, 프로젝트 폴더에 반영한다"],
  ];
  const cw = (W - 0.18 * 3) / 4, y = 1.75, h = 3.25;
  steps.forEach(([n, title, desc], i) => {
    const cx = X + i * (cw + 0.18);
    s.addShape(pres.shapes.RECTANGLE, { x: cx, y, w: cw, h, fill: { color: C.canvas }, line: { color: C.hairline, width: 0.75 } });
    text(s, n, { x: cx + 0.3, y: y + 0.3, w: 1, h: 0.28, fontFace: F.semi, fontSize: 12, bold: true, color: C.primary, charSpacing: 2, valign: "middle" });
    text(s, title, { x: cx + 0.3, y: y + 0.66, w: cw - 0.6, h: 0.5, fontFace: F.semi, fontSize: 22, color: C.ink, valign: "middle" });
    text(s, desc, { x: cx + 0.3, y: y + 1.3, w: cw - 0.6, h: 1.6, fontSize: 15, color: C.ink80, lineSpacingMultiple: 1.5 });
  });
  dataStrip(s, 5.45, [
    { value: `${data.rounds.length}회`, label: "QA 회차" },
    { value: `${allItems.length}건`, label: "발견한 이슈" },
    { value: `${fixed}건`, label: "수정 완료" },
    { value: `${openRisks}건`, label: "남은 위험 · 확인 필요" },
  ]);
  footer(s, p, t);
});

// 3. 현황
plan.push((s, p, t) => {
  s.background = { color: C.parchment };
  grid(s, "01  OVERVIEW", "회차별 이슈 현황");
  const cats = data.rounds.map((r) => `${r.id}`);
  const series = ["High", "Med", "Low"].map((k) => ({ name: SEV[k], labels: cats, values: data.rounds.map((r) => sevCount(r.items)[k]) }));
  s.addChart(pres.charts.BAR, series, {
    x: X - 0.1, y: 1.75, w: 6.2, h: 4.9,
    barDir: "col", barGrouping: "stacked", barGapWidthPct: 110,
    chartColors: [C.primary, C.blueMid, C.grayMid],
    showLegend: true, legendPos: "b", legendFontFace: F.reg, legendFontSize: 11, legendColor: C.ink80,
    catAxisLabelFontFace: F.semi, catAxisLabelFontSize: 12, catAxisLabelColor: C.ink, catAxisLineShow: false,
    catAxisMajorTickMark: "none", valAxisHidden: true, valGridLine: { style: "none" },
    showValue: true, dataLabelPosition: "ctr", dataLabelFontFace: F.semi, dataLabelFontSize: 11, dataLabelColor: "FFFFFF", dataLabelFormatCode: "0;;;",
    showTitle: false, plotArea: { fill: { type: "none" } },
  });
  // 오른쪽: 회차 카드
  const cx = 6.867, cw = 5.8, gap = 0.14, n = data.rounds.length;
  // 회차가 늘면 카드를 낮춘다 (5.05" 안에 모두 들어가게). 낮은 카드에서는 세부 줄을 뺀다.
  const ch = Math.min(1.1, (5.05 - gap * (n - 1)) / n);
  const compact = ch < 1.02;
  data.rounds.forEach((r, i) => {
    const cy = 1.75 + i * (ch + gap);
    const c = sevCount(r.items);
    s.addShape(pres.shapes.RECTANGLE, { x: cx, y: cy, w: cw, h: ch, fill: { color: C.canvas }, line: { color: C.hairline, width: 0.75 } });
    const idLine = compact ? `${r.id}  ${r.date}   ·   높음 ${c.High} · 중간 ${c.Med} · 낮음 ${c.Low}` : `${r.id}  ${r.date}`;
    text(s, idLine, { x: cx + 0.3, y: cy + 0.1, w: cw - 2.2, h: 0.26, fontFace: F.semi, fontSize: 12, bold: true, color: C.primary, charSpacing: 0, valign: "middle" });
    text(s, r.title, { x: cx + 0.3, y: cy + (compact ? 0.36 : 0.4), w: cw - 2.2, h: 0.42, fontFace: F.semi, fontSize: compact ? 18 : 19, color: C.ink, valign: "middle" });
    if (!compact)
      text(s, `이슈 ${r.items.length}건  ·  높음 ${c.High} · 중간 ${c.Med} · 낮음 ${c.Low}`,
           { x: cx + 0.3, y: cy + 0.8, w: cw - 0.6, h: 0.24, fontSize: 11.5, color: C.ink48, valign: "middle" });
    text(s, `${r.items.filter((x) => x.status === "수정").length}/${r.items.length}`,
         { x: cx + cw - 1.8, y: cy + ch / 2 - 0.3, w: 1.5, h: 0.45, fontFace: F.semi, fontSize: 24, color: C.ink, align: "right", valign: "middle" });
    text(s, "수정", { x: cx + cw - 1.8, y: cy + ch / 2 + 0.14, w: 1.5, h: 0.22, fontSize: 11, color: C.ink48, align: "right", valign: "middle" });
  });
  footer(s, p, t);
});

// 4. 회차별
data.rounds.forEach((r) => {
  const c = sevCount(r.items);
  plan.push((s, p, t) => {
    s.background = { color: C.tile };
    grid(s, `${r.id}  ·  ${r.date}`, r.title, true);
    text(s, "계기", { x: X, y: 1.85, w: 1.2, h: 0.35, fontFace: F.semi, fontSize: 13, color: "8E8E93", valign: "middle" });
    text(s, r.trigger, { x: X + 1.2, y: 1.85, w: W - 1.2, h: 0.35, fontSize: 18, color: C.onDark, valign: "middle" });
    text(s, "방법", { x: X, y: 2.35, w: 1.2, h: 0.6, fontFace: F.semi, fontSize: 13, color: "8E8E93" });
    text(s, r.method, { x: X + 1.2, y: 2.3, w: W - 1.2, h: 0.8, fontSize: 18, color: C.onDarkMuted, lineSpacingMultiple: 1.3 });
    const cells = r.metrics && r.metrics.length
      ? r.metrics.map((m) => ({ value: m.value, label: m.label, sub: m.sub }))
      : [{ value: `${r.items.length}건`, label: "발견한 이슈" }, { value: `${c.High}`, label: "높음" }, { value: `${c.Med}`, label: "중간" }, { value: `${c.Low}`, label: "낮음" }];
    if (r.metrics && r.metrics.length) cells.unshift({ value: `${r.items.length}건`, label: "발견한 이슈", sub: `높음 ${c.High} · 중간 ${c.Med} · 낮음 ${c.Low}` });
    dataStrip(s, 5.3, cells, true);
    footer(s, p, t, true);
  });

  const colW = [0.8, 1.3, 4.6, 4.3, 1.0];
  const rows = r.items.map((it) => [SEV[it.sev] || it.sev, it.area, it.problem, it.fix, it.status === "수정" ? "✓ 수정" : it.status]);
  const pages = paginate(rows, colW, 6.8 - 1.75 - 0.36);
  pages.forEach((pg, pi) => {
    plan.push((s, p, t) => {
      s.background = { color: C.canvas };
      grid(s, `${r.id}  ·  ${r.title}`, `발견한 문제와 조치${pages.length > 1 ? `  (${pi + 1}/${pages.length})` : ""}`);
      table(s, 1.75, ["심각도", "영역", "문제", "조치", "상태"], colW, pg, (i, cell) => {
        if (i === 0) return cell === "높음" ? { fontFace: F.semi, bold: true, color: C.primary } : cell === "중간" ? { fontFace: F.med, color: C.ink } : { color: C.ink48 };
        if (i === 1) return { fontFace: F.semi, color: C.ink };
        if (i === 4) return { fontFace: F.med, color: C.primary, align: "center" };
        return {};
      });
      // 마지막 쪽에 자리가 남으면 이 회차 요약 띠
      if (pi === pages.length - 1 && 1.75 + pageHeight(pg) + 0.25 + 1.05 <= 6.95) {
        const done = r.items.filter((x) => x.status === "수정").length;
        dataStrip(s, 5.75, [
          { value: `${r.items.length}건`, label: "이 회차에서 발견" },
          { value: `${c.High}건`, label: "높음" },
          { value: `${c.Med + c.Low}건`, label: "중간 · 낮음" },
          { value: `${done}/${r.items.length}`, label: "수정 완료" },
        ]);
      }
      footer(s, p, t);
    });
  });

  if (r.chart) {
    plan.push((s, p, t) => {
      s.background = { color: C.parchment };
      grid(s, `${r.id}  ·  ${r.title}`, "밸런스 재조정 — 구역 도달 시각");
      s.addChart(pres.charts.BAR, r.chart.series.map((sr) => ({ name: sr.name, labels: r.chart.categories, values: sr.values })), {
        x: X - 0.1, y: 1.75, w: 7.6, h: 5.0, barDir: "col", barGrouping: "clustered", barGapWidthPct: 70,
        chartColors: [C.grayMid, C.ink48, C.primary],
        showLegend: true, legendPos: "b", legendFontFace: F.reg, legendFontSize: 11, legendColor: C.ink80,
        catAxisLabelFontFace: F.semi, catAxisLabelFontSize: 12, catAxisLabelColor: C.ink, catAxisLineShow: false, catAxisMajorTickMark: "none",
        valAxisHidden: true, valGridLine: { style: "none" },
        showValue: true, dataLabelPosition: "outEnd", dataLabelFontFace: F.semi, dataLabelFontSize: 10, dataLabelColor: C.ink80, dataLabelFormatCode: "0.0",
        showTitle: false, plotArea: { fill: { type: "none" } },
      });
      const rx = 8.55, rw = 4.1;
      text(s, r.chart.title, { x: rx, y: 1.8, w: rw, h: 0.3, fontFace: F.semi, fontSize: 11, bold: true, color: C.ink48 });
      text(s, "시뮬레이터는 '구역 평균 재화 × 크기 보너스'로 수입을 셌지만, 게임은 먹은 종의 재화만 준다", { x: rx, y: 2.2, w: rw, h: 1.1, fontFace: F.med, fontSize: 16, color: C.ink, lineSpacingMultiple: 1.4 });
      text(s, "나보다 큰 상어·범고래가 평균을 끌어올려 수입이 2~3배 부풀었고, 게임 그대로면 클리어에 약 12시간이 걸렸다. 먹은 종 단위로 다시 계산한 뒤 구역 배율을 올려 4.5시간으로 되돌렸다",
           { x: rx, y: 3.4, w: rw, h: 1.9, fontSize: 13, color: C.ink80, lineSpacingMultiple: 1.5 });
      s.addShape(pres.shapes.LINE, { x: rx, y: 5.45, w: rw, h: 0, line: { color: C.hairline, width: 0.75 } });
      text(s, r.chart.note, { x: rx, y: 5.6, w: rw, h: 0.8, fontFace: F.med, fontSize: 13, color: C.primary, lineSpacingMultiple: 1.4 });
      footer(s, p, t);
    });
  }
});

// 5. 남은 위험
{
  const colW = [1.3, 4.6, 3.9, 1.2, 1.0];
  const rows = data.risks.map((r) => [r.area, r.risk, r.next, r.status, r.from]);
  const pages = paginate(rows, colW, 6.8 - 1.75 - 0.36);
  pages.forEach((pg, pi) => {
    plan.push((s, p, t) => {
      s.background = { color: C.canvas };
      grid(s, "NEXT  OPEN RISKS", `남은 위험 · 플레이테스트 체크리스트${pages.length > 1 ? `  (${pi + 1}/${pages.length})` : ""}`);
      table(s, 1.75, ["영역", "위험", "다음 조치", "상태", "출처"], colW, pg, (i, cell) => {
        if (i === 0) return { fontFace: F.semi, color: C.ink };
        if (i === 3) return cell === "확인 필요" ? { fontFace: F.semi, bold: true, color: C.primary, align: "center" } : { color: C.ink48, align: "center" };
        if (i === 4) return { color: C.ink48, align: "center" };
        return {};
      });
      if (pi === pages.length - 1 && 1.75 + pageHeight(pg) + 0.25 + 1.05 <= 6.95) {
        const need = data.risks.filter((x) => x.status === "확인 필요").length;
        dataStrip(s, 5.75, [
          { value: `${openRisks}건`, label: "남은 위험" },
          { value: `${need}건`, label: "플레이테스트로 확인 필요" },
          { value: `${data.risks.filter((x) => x.status === "미정").length}건`, label: "조치 방법 미정" },
          { value: data.updated, label: "마지막 갱신" },
        ]);
      }
      footer(s, p, t);
    });
  });
}

// ── 그리기 ──────────────────────────────────────────────────
plan.forEach((draw, i) => draw(pres.addSlide(), i + 1, plan.length));
pres.writeFile({ fileName: OUT }).then((f) => console.log(`${f}  (${plan.length}장)`));
