// 页内采集器：把每个文字 run 的横向取值带（x0/x1）与纵向搜索窗口（y0/y1）
// 用 CSS px 报回主进程。measure.cjs 用 Function.prototype.toString 把本函数
// 序列化后注入渲染层，所以这里必须是自包含的普通函数（不引用任何外层变量），
// 且只用字符串拼接，不用模板字面量。
module.exports = function collect() {
  const runs = [];
  const mctx = document.createElement('canvas').getContext('2d');

  function band(key, group, rect, text, el) {
    if (!rect || !(rect.width > 0) || !(rect.height > 0)) return;
    const cs = el ? getComputedStyle(el) : null;
    const str = String(text == null ? '' : text).trim();
    let met = {};
    if (cs) {
      mctx.font = cs.fontStyle + ' ' + cs.fontWeight + ' ' + cs.fontSize + ' ' + cs.fontFamily;
      const m = mctx.measureText(str);
      met = {
        A: +m.fontBoundingBoxAscent.toFixed(3),
        D: +m.fontBoundingBoxDescent.toFixed(3),
        a: +m.actualBoundingBoxAscent.toFixed(3),
        d: +m.actualBoundingBoxDescent.toFixed(3)
      };
    }
    runs.push({
      key: key,
      group: group,
      text: str,
      em: cs ? parseFloat(cs.fontSize) : null,
      weight: cs ? cs.fontWeight : null,
      lineHeight: cs ? cs.lineHeight : null,
      va: cs ? cs.verticalAlign : null,
      tf: cs ? cs.transform : null,
      rectH: rect.height,
      ...met,
      x0: rect.left,
      x1: rect.right,
      y0: rect.top,
      y1: rect.bottom,
      fontSize: null
    });
  }

  function rectFrom(l, r, t, b) {
    return { left: l, right: r, top: t, bottom: b, width: r - l, height: b - t };
  }

  function textNodeRect(node, end) {
    const r = document.createRange();
    r.setStart(node, 0);
    r.setEnd(node, end);
    return r.getBoundingClientRect();
  }

  function fontOf(el) {
    const cs = getComputedStyle(el);
    return { font: cs.fontStyle + ' ' + cs.fontWeight + ' ' + cs.fontSize + ' ' + cs.fontFamily, size: parseFloat(cs.fontSize) };
  }

  const mode = document.body.dataset.dsmMode || (document.querySelector('.bar') ? 'widget' : 'settings');

  if (mode === 'widget') {
    const bar = document.querySelector('.bar');
    if (bar) {
      const segs = Array.prototype.filter.call(bar.children, function (el) {
        return el.classList && el.classList.contains('seg');
      });
      segs.forEach(function (seg) {
        const label = seg.querySelector(':scope > .label');
        const name = label ? label.textContent.trim() : '时间';
        if (label) band('label', name, label.getBoundingClientRect(), label.textContent, label);
        const readings = Array.prototype.slice.call(seg.querySelectorAll('.reading'));
        readings.forEach(function (rd, ri) {
          const g = name + (readings.length > 1 ? ' ' + (ri + 1) : '');
          const num = rd.querySelector('.num');
          const unit = rd.querySelector('.unit');
          if (!num) return;
          const numRect = num.getBoundingClientRect();
          const dir = num.querySelector('.direction');
          if (dir) {
            const dRect = dir.getBoundingClientRect();
            band('arrow', g, dRect, dir.textContent, dir);
            band('num', g, rectFrom(dRect.right, numRect.right, numRect.top, numRect.bottom),
              num.textContent.replace(dir.textContent, ''), num);
          } else {
            band('num', g, numRect, num.textContent, num);
          }
          if (unit) band('unit', g, unit.getBoundingClientRect(), unit.textContent, unit);
        });
        const clock = seg.querySelector('.clock');
        if (clock) band('clock', '时间', clock.getBoundingClientRect(), clock.textContent, clock);
      });
    }
  } else {
    const rows = Array.prototype.slice.call(document.querySelectorAll('.row'));
    rows.forEach(function (row, i) {
      const span = row.querySelector(':scope > span');
      let name = 'row' + i;
      if (span) {
        name = (span.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 12);
        const bold = span.querySelector('b');
        const tn = span.firstChild;
        if (tn && tn.nodeType === 3) {
          const txt = tn.textContent.replace(/\s+$/, '');
          band('label', name, textNodeRect(tn, txt.length), txt, span);
        }
        if (bold) band('value', name, bold.getBoundingClientRect(), bold.textContent, bold);
      }
      const sel = row.querySelector('select');
      if (sel) {
        const r = sel.getBoundingClientRect();
        const f = fontOf(sel);
        const cs = getComputedStyle(sel);
        const opt = sel.options[sel.selectedIndex];
        const probe = document.createElement('canvas').getContext('2d');
        probe.font = f.font;
        const w = Math.max(4, probe.measureText(opt ? opt.text : '').width);
        const x0 = r.left + parseFloat(cs.paddingLeft || '0') + 1;
        band('option', name, rectFrom(x0, x0 + w, r.top, r.bottom), opt ? opt.text : '', sel);
      }
    });
  }

  // 卡片自身的盒子：裁切判据要用它。真实窗口高 == WIDGET_CARD_HEIGHT，所以墨迹
  // 一旦越出内容盒（border-box 内缩 1px）就是真会被切掉的部分。
  var cardEl = document.querySelector('.bar');
  var cardRect = cardEl ? cardEl.getBoundingClientRect() : null;

  return {
    mode: mode,
    card: cardRect
      ? { top: cardRect.top, bottom: cardRect.bottom, height: cardRect.height, left: cardRect.left, right: cardRect.right, width: cardRect.width }
      : null,
    dpr: window.devicePixelRatio,
    zoom: visualViewport ? visualViewport.scale : 1,
    scroll: { x: window.scrollX, y: window.scrollY },
    viewport: { w: window.innerWidth, h: window.innerHeight },
    runs: runs
  };
};
