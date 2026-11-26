// Optional display-only filter for the inspected legacy x86 chat renderer.
function photoMatchesSenderGroup(group, x, y) {
  // Text messages may sit between the sender label and the first photo.
  // The next avatar ends the group; vertical distance alone must not end it.
  return Boolean(group && Math.abs(x - group.x) <= group.tolerance &&
    y >= group.nextY - group.tolerance);
}

function photoHoverFilter() {
  const unavailable = {configure() {}, tick() {}, restore() {}, status: () => ({available: false})};
  if (Process.arch !== 'ia32') return unavailable;

  const photoCall = app.base.add(0x135e7f3);
  const imageDraw = app.base.add(0x13ab370);
  if (photoCall.readU8() !== 0xe8 || !photoCall.add(5).add(photoCall.add(1).readS32()).equals(imageDraw) ||
      photoCall.add(5).readU32() !== 0x8bde458a) return unavailable;
  if (Array.from(new Uint8Array(app.base.add(0x1334f70).readByteArray(7))).join(',') !== '85,139,236,139,85,8,86') return unavailable;

  const begin = user.getExportByName('BeginPaint');
  const end = user.getExportByName('EndPaint');
  const readTitle = fn(user, 'GetWindowTextW', 'int', ['pointer', 'pointer', 'int']);
  const getParent = fn(user, 'GetParent', 'pointer', ['pointer']);
  const cursor = fn(user, 'GetCursorPos', 'int', ['pointer']);
  const clientPoint = fn(user, 'ScreenToClient', 'int', ['pointer', 'pointer']);
  const atPoint = fn(user, 'WindowFromPoint', 'pointer', ['int', 'int']);
  const fill = fn(user, 'FillRect', 'int', ['pointer', 'pointer', 'pointer']);
  const brush = fn(user, 'GetSysColorBrush', 'pointer', ['int'])(15);
  const getDC = fn(user, 'GetDC', 'pointer', ['pointer']);
  const releaseDC = fn(user, 'ReleaseDC', 'int', ['pointer', 'pointer']);
  const rect = Memory.alloc(16);
  const paintBuffer = Memory.alloc(512);
  const point = Memory.alloc(8);
  const profileReturn = app.base.add(0x780ef0);
  const photoReturn = photoCall.add(5);
  const textReturn = app.base.add(0xf1ce5b);
  const blendReturn = app.base.add(0x13ab46e);
  const compact = compactMedia();
  let photoNode = null, hidePhotos = false;
  let room = '', sender = '', paint = null, draw = null, hovered = null;
  let masked = 0, revealed = 0;
  const regions = new Map();

  function mouseIn(window) {
    if (!cursor(point) || !atPoint(point.readS32(), point.add(4).readS32()).equals(window) ||
        !clientPoint(window, point)) return null;
    return [point.readS32(), point.add(4).readS32()];
  }
  function inside(mouse, r) {
    return mouse && mouse[0] >= r.x && mouse[1] >= r.y &&
      mouse[0] < r.x + r.w && mouse[1] < r.y + r.h;
  }
  function repaint(r) {
    invalidate(r.window, ptr(0), 0);
  }

  listeners.push(Interceptor.attach(begin, {onEnter(args) {
    if (!room || this.threadId !== uiThread || paint) return;
    const window = args[0], root = getParent(window);
    if (!watchedWindows.some(w => w.equals(root))) return;
    refreshClass(window, paintBuffer, 256);
    if (paintBuffer.readUtf16String() !== 'EVA_VH_ListControl_Dblclk') return;
    if (!readTitle(root, paintBuffer, 256) || paintBuffer.readUtf16String() !== room) return;
    const key = window.toString();
    regions.set(key, []);
    paint = {window, key, avatar: null, group: null, mouse: mouseIn(window), covers: []};
  }}));
  listeners.push(Interceptor.attach(end, {onEnter(args) {
    if (this.threadId !== uiThread || !paint || !args[0].equals(paint.window)) return;
    if (paint.covers.length) {
      const dc = getDC(paint.window);
      if (!dc.isNull()) {
        for (const r of paint.covers) {
          rect.writeS32(r.x); rect.add(4).writeS32(r.y);
          rect.add(8).writeS32(r.x + r.w); rect.add(12).writeS32(r.y + r.h);
          fill(dc, rect, brush);
        }
        releaseDC(paint.window, dc);
      }
    }
    paint = null;
  }}));
  listeners.push(Interceptor.attach(imageDraw, {
    onEnter() {
      if (this.threadId !== uiThread || !paint) return;
      if (this.returnAddress.equals(profileReturn)) this.kind = 'profile';
      if (this.returnAddress.equals(photoReturn)) {
        photoNode = this.context.edi;
        if (paint.group || compact.has(photoNode)) this.kind = 'photo';
      }
      if (this.kind) {
        this.previousDraw = draw;
        draw = this.kind;
      }
    },
    onLeave() { if (this.kind) draw = this.previousDraw; }
  }));
  listeners.push(Interceptor.attach(user.getExportByName('DrawTextExW'), {onEnter(args) {
    if (this.threadId !== uiThread || !paint?.avatar || !this.returnAddress.equals(textReturn)) return;
    const bounds = args[3], n = args[2].toInt32();
    if (bounds.isNull() || args[1].isNull() || n < -1 || n === 0 || n > 255) return;
    const x = bounds.readS32(), y = bounds.add(4).readS32();
    const avatar = paint.avatar;
    if (y !== avatar.y || x < avatar.x + avatar.w || x > avatar.x + avatar.w * 2) return;
    paint.avatar = null;
    const label = args[1].readUtf16String(n < 0 ? undefined : n);
    const name = (label || '').split('(')[0].trim();
    if (sender.split(',').map(s => s.split('(')[0].trim()).includes(name)) {
      paint.group = {x, nextY: y + avatar.w * 0.5, tolerance: avatar.w * 0.2};
    }
  }}));
  listeners.push(Interceptor.attach(blend, {onEnter(args) {
    if (this.threadId !== uiThread || !paint || !draw || !this.returnAddress.equals(blendReturn)) return;
    const x = args[1].toInt32(), y = args[2].toInt32(), w = args[3].toInt32(), h = args[4].toInt32();
    if (draw === 'profile') {
      paint.group = null;
      paint.avatar = {x, y, w};
      return;
    }
    if (w < 30 || h < 30) return;
    const group = paint.group;
    if (!compact.has(photoNode) && !photoMatchesSenderGroup(group, x, y)) {
      paint.group = null;
      return;
    }
    if (group) group.nextY = y + h + group.tolerance / 2;
    compact.remember(photoNode);
    const r = {x, y, w, h, window: paint.window};
    regions.get(paint.key)?.push(r);
    if (!hidePhotos || inside(paint.mouse, r)) {
      revealed++;
      return;
    }
    // Suppress the source before compositing. Painting over the intermediate DC
    // afterwards can lose its alpha and leave the original photo visible.
    args[10] = ptr(args[10].toUInt32() & 0xff00ffff);
    paint.covers.push(r);
    masked++;
  }}));

  return {
    configure(nextRoom, nextSender, nextHide = false) {
      if (room === nextRoom && sender === nextSender && hidePhotos === nextHide) return;
      if (room !== nextRoom || sender !== nextSender) compact.restore();
      for (const list of regions.values()) if (list.length) repaint(list[0]);
      regions.clear();
      hovered = null;
      room = nextRoom;
      sender = nextSender;
      hidePhotos = nextHide;
      if (room) for (const w of watchedWindows) refreshConversation(w);
    },
    tick() {
      if (!room || !hidePhotos) return;
      let next = null;
      for (const [key, list] of regions) {
        if (!list.length || !watchedWindows.some(w => w.equals(getParent(list[0].window)))) {
          regions.delete(key);
          continue;
        }
        const mouse = mouseIn(list[0].window);
        next = list.find(r => inside(mouse, r)) || next;
      }
      if (next?.window.toString() === hovered?.window.toString() &&
          next?.x === hovered?.x && next?.y === hovered?.y && next?.w === hovered?.w && next?.h === hovered?.h) return;
      if (hovered) repaint(hovered);
      if (next && (!hovered || !next.window.equals(hovered.window))) repaint(next);
      hovered = next;
    },
    restore() { room = ''; compact.restore(true); },
    status: () => ({available: true, enabled: Boolean(room), masked, revealed, ...compact.status()})
  };
}
