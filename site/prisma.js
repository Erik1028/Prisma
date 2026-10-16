// The "pretend PC" in the header: a fan ring, four RAM sticks, a motherboard, a GPU and a mouse, all lit by
// one 25 fps loop, like the app. The effect maths is ported line for line from MainForm.BuildColorsCore
// (speed = slider / 25, so the default 50 is 2.0; brightness 100%; a fan ring has 12 LEDs).
// Strobe is left out on purpose (no flashing on a web page); Ambient and Music need your screen and sound.
(function () {
  var rig = document.getElementById('rig');
  if (!rig) return;
  var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ---- devices: each has its own LED count, exactly as the app treats them -------------------------
  function leds(parent, n, cls) {
    var out = [];
    for (var i = 0; i < n; i++) { var e = document.createElement('i'); if (cls) e.className = cls; parent.appendChild(e); out.push(e); }
    return out;
  }
  var ring = rig.querySelector('.ring');
  var fan = leds(ring, 12);
  fan.forEach(function (e, i) { e.style.setProperty('--a', (i * 30) + 'deg'); });
  var sticks = Array.prototype.map.call(rig.querySelectorAll('.sticks > i'), function (s) { return leds(s, 5); });
  var mb = leds(rig.querySelector('.zones'), 5);
  var gpu = leds(rig.querySelector('.bar'), 1);
  var mouse = leds(rig.querySelector('.blob'), 1);
  var devices = [fan].concat(sticks, [mb, gpu, mouse]).map(function (l) { return { leds: l, fade: { r: -1, g: 0, b: 0 } }; });

  // ---- the app's maths -------------------------------------------------------------------------------
  var RING = 12, SPEED = 50 / 25, MASTER = 1;
  function frac(x) { return x - Math.floor(x); }
  function hsv(h, s, v) {
    var c = v * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = v - c, r, g, b, k = h / 60;
    if (k < 1) { r = c; g = x; b = 0; } else if (k < 2) { r = x; g = c; b = 0; } else if (k < 3) { r = 0; g = c; b = x; }
    else if (k < 4) { r = 0; g = x; b = c; } else if (k < 5) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
    return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
  }
  function scaled(c, f) { return [c[0] * f | 0, c[1] * f | 0, c[2] * f | 0]; }
  var GRAD_END = [150, 80, 255];
  function fadeTo(fade, target, k) {
    if (fade.r < 0) { fade.r = target[0]; fade.g = target[1]; fade.b = target[2]; }
    fade.r += (target[0] - fade.r) * k; fade.g += (target[1] - fade.g) * k; fade.b += (target[2] - fade.b) * k;
    return [Math.round(fade.r), Math.round(fade.g), Math.round(fade.b)];
  }
  var FX = {
    'Static': function (n, t, base, fade) { var c = fadeTo(fade, scaled(base, MASTER), 0.30); return fill(n, c); },
    'Rainbow wave': function (n, t) {
      var step = 360 / Math.min(Math.max(n, 8), RING), out = [];
      for (var i = 0; i < n; i++) out.push(hsv((((t * SPEED * 60 + i * step) % 360) + 360) % 360, 1, MASTER));
      return out;
    },
    'Colour cycle': function (n, t) { return fill(n, hsv((t * SPEED * 30) % 360, 1, MASTER)); },
    'Breathing': function (n, t, base) { return fill(n, scaled(base, MASTER * (0.04 + 0.96 * (Math.sin(t * SPEED * 2.2) + 1) / 2))); },
    'Gradient': function (n, t, base) {
      var sweep = (1 - Math.cos(t * SPEED * 0.9)) / 2, out = [];
      for (var i = 0; i < n; i++) {
        var f = n <= 1 ? sweep : i / (n - 1);
        out.push([0, 1, 2].map(function (k) { return Math.max(0, Math.min(255, (base[k] + (GRAD_END[k] - base[k]) * f) * MASTER)) | 0; }));
      }
      return out;
    },
    'Comet': function (n, t, base) {
      var head = (t * SPEED * 0.6 * n) % n, tail = Math.max(2, n * 0.35), out = [];
      for (var i = 0; i < n; i++) { var d = head - i; if (d < 0) d += n; var b = Math.max(0, 1 - d / tail); out.push(scaled(base, MASTER * b * b)); }
      return out;
    },
    'Twinkle': function (n, t, base) {
      var out = [];
      for (var i = 0; i < n; i++) {
        var seed = frac(Math.sin((i + 1) * 12.9898) * 43758.5453), tw = Math.sin(t * SPEED * 2.5 + seed * Math.PI * 2);
        out.push(scaled(base, MASTER * (0.05 + 0.95 * Math.pow(Math.max(0, tw), 6))));
      }
      return out;
    },
    'Fire': function (n, t) {
      var out = [];
      for (var i = 0; i < n; i++) {
        var n1 = Math.sin(t * SPEED * 7 + i * 1.7), n2 = Math.sin(t * SPEED * 13 + i * 0.9 + 2.1);
        var flick = Math.max(0.08, Math.min(1, 0.55 + 0.45 * (n1 * 0.6 + n2 * 0.4)));
        out.push(hsv(14 + 26 * frac(Math.sin((i + 1) * 91.7) * 1234.5 + t * 0.07), 1, MASTER * flick));
      }
      return out;
    },
    'Surprise': function (n, t, base, fade) {
      var seg = Math.floor(t * SPEED * 0.33), hue = frac(Math.sin(seg * 78.233) * 43758.5453) * 360;
      return fill(n, fadeTo(fade, hsv(hue, 0.9, MASTER), 0.05));
    }
  };
  function fill(n, c) { var out = []; for (var i = 0; i < n; i++) out.push(c); return out; }

  // ---- controls ----------------------------------------------------------------------------------------
  var SWATCHES = [['Teal', [0, 200, 170]], ['Red', [255, 0, 0]], ['Green', [0, 255, 0]], ['Blue', [0, 0, 255]],
                  ['Orange', [255, 165, 0]], ['Magenta', [255, 0, 255]], ['White', [255, 255, 255]]];
  var effect = 'Rainbow wave', base = SWATCHES[0][1];
  function radio(box, items, current, onPick, isSwatch) {
    var buttons = items.map(function (it) {
      var b = document.createElement('button');
      b.type = 'button'; b.setAttribute('role', 'radio');
      if (isSwatch) { b.className = 'swatch'; b.style.setProperty('--c', 'rgb(' + it[1].join(',') + ')'); b.setAttribute('aria-label', it[0]); b.title = it[0]; }
      else b.textContent = it;
      b.addEventListener('click', function () { pick(b); onPick(it); });
      box.appendChild(b);
      return b;
    });
    function pick(b) { buttons.forEach(function (x) { var on = x === b; x.setAttribute('aria-checked', on); x.tabIndex = on ? 0 : -1; }); }
    pick(buttons[items.indexOf(current)]);
    box.addEventListener('keydown', function (e) {
      var i = buttons.indexOf(document.activeElement), d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
      if (i < 0 || !d) return;
      e.preventDefault(); var nb = buttons[(i + d + buttons.length) % buttons.length]; nb.focus(); nb.click();
    });
  }
  radio(rig.querySelector('.fx'), Object.keys(FX), effect, function (e) { effect = e; devices.forEach(function (d) { d.fade.r = -1; }); if (reduce) paint(1.3); });
  radio(rig.querySelector('.sw'), SWATCHES, SWATCHES[0], function (s) { base = s[1]; if (reduce) paint(1.3); }, true);

  // ---- the loop: 25 fps, paused while the rig is off screen or the tab is hidden ---------------------
  function paint(t) {
    devices.forEach(function (d) {
      var cols = FX[effect](d.leds.length, t, base, d.fade);
      d.leds.forEach(function (e, i) {
        var c = cols[i], lit = c[0] + c[1] + c[2] > 18;
        e.style.setProperty('--c', lit ? 'rgb(' + c.join(',') + ')' : '#1d2026');
        e.classList.toggle('lit', lit);
      });
    });
  }
  if (reduce) { paint(1.3); return; }
  var visible = true, t0 = performance.now(), last = 0, raf = 0;
  function frame(now) {
    raf = 0;
    if (!visible || document.hidden) return;
    if (now - last >= 40) { last = now; paint((now - t0) / 1000); }
    raf = requestAnimationFrame(frame);
  }
  function wake() { if (!raf && visible && !document.hidden) raf = requestAnimationFrame(frame); }
  if ('IntersectionObserver' in window) {
    new IntersectionObserver(function (es) { visible = es[0].isIntersecting; wake(); }).observe(rig);
  }
  document.addEventListener('visibilitychange', wake);
  paint(0); wake();
})();
