// Native layout changes run between messages on the owning UI thread.
function compactMedia() {
  const entries = new Map();
  const pending = new Map();
  const slots = Memory.alloc(512 * Process.pointerSize);
  const slotLimit = Memory.alloc(4);
  const slotOwners = new Map();
  const wakeMessage = fn(user, 'RegisterWindowMessageW', 'uint', ['pointer'])(Memory.allocUtf16String('KakaoAutoPlay.CompactLayout'));
  let restoring = false, stopped = false;
  let applied = 0, layoutMs = 0;
  const setHeight = new NativeFunction(app.base.add(0x1334f70), 'void', ['pointer', 'int'], 'thiscall');
  const post = fn(user, 'PostMessageW', 'int', ['pointer', 'uint', 'pointer', 'pointer']);
  const postThread = fn(user, 'PostThreadMessageW', 'int', ['uint', 'uint', 'pointer', 'pointer']);
  function wake() {
    const delivered = watchedWindows.map(window => post(window, wakeMessage, ptr(0), ptr(0))).some(Boolean);
    if (!delivered) postThread(uiThread, wakeMessage, ptr(0), ptr(0));
  }
  function dimensions(node, width, height) {
    node.add(0xa8).writeS32(width);
    node.add(0x94).writeU32(node.add(0x94).readU32() & ~0x10);
    const table = node.readPointer();
    new NativeFunction(table.add(0x58).readPointer(), 'bool', ['pointer', 'int'], 'thiscall')(node, -1);
    new NativeFunction(table.add(0x60).readPointer(), 'bool', ['pointer'], 'thiscall')(node);
    setHeight(node, height);
  }
  function alive(row) {
    return slots.add(row.slot * Process.pointerSize).readPointer().equals(row.node);
  }
  function forget(row) {
    slots.add(row.slot * Process.pointerSize).writePointer(ptr(0));
    entries.delete(row.node.toString());
    pending.delete(row.node.toString());
    slotOwners.delete(row.slot);
  }
  const consume = new NativeCallback(() => {
    if (Process.getCurrentThreadId() !== uiThread) return;
    if (restoring) {
      restoring = false;
      pending.clear();
      const saved = [...entries.values()];
      for (const row of saved) {
        if (alive(row) && row.node.readPointer().equals(app.base.add(0x20fec18))) dimensions(row.node, row.width, row.height);
        forget(row);
      }
      for (const window of watchedWindows) refreshConversation(window);
      return;
    }
    if (stopped) return;
    const changes = [...pending.values()];
    pending.clear();
    const started = Date.now();
    for (const row of changes) {
      if (!alive(row)) { forget(row); continue; }
      const scale = Math.min(1, 64 / Math.max(row.width, row.height));
      dimensions(row.node, Math.max(1, Math.round(row.width * scale)), Math.max(1, Math.round(row.height * scale)));
      applied++;
    }
    if (changes.length) for (const window of watchedWindows) refreshConversation(window);
    if (changes.length) layoutMs += Date.now() - started;
  }, 'void', []);
  const module = new CModule(`
#include <gum/guminterceptor.h>
extern gpointer live_slots[512];
extern guint32 slot_limit;
extern void consume(void);
void freed(GumInvocationContext *ctx) {
  gsize base=(gsize)gum_invocation_context_get_nth_argument(ctx,2);
  for(guint32 i=0;i<slot_limit;i++) {
    gsize p=(gsize)live_slots[i];
    if(p && p>=base && p-base<32) live_slots[i]=NULL;
  }
}
void dispatch(GumInvocationContext *ctx) {
  guint32 *msg=gum_invocation_context_get_nth_argument(ctx,0);
  if(msg && msg[1]==${wakeMessage}) consume();
}
`, {live_slots: slots, slot_limit: slotLimit, consume});
  listeners.push(Interceptor.attach(Process.getModuleByName('ntdll.dll').getExportByName('RtlFreeHeap'), module.freed));
  for (const name of ['DispatchMessageW', 'DispatchMessageA'])
    listeners.push(Interceptor.attach(user.getExportByName(name), module.dispatch));
  return {
    nativeModule: module,
    nativeCallback: consume,
    has(node) {
      const row = entries.get(node.toString());
      if (row && !alive(row)) forget(row);
      return Boolean(row && alive(row));
    },
    remember(node) {
      if (stopped || restoring || entries.has(node.toString())) return;
      for (const row of entries.values()) if (!alive(row)) forget(row);
      if (entries.size >= 512) return;
      if (!node.readPointer().equals(app.base.add(0x20fec18))) return;
      const width = node.add(0xa8).readS32(), height = node.add(0xac).readS32();
      if (width < 1 || height < 1 || width > 4096 || height > 4096) return;
      let slot = 0;
      while (slotOwners.has(slot)) slot++;
      const row = {node, width, height, slot};
      slotOwners.set(slot, row);
      slots.add(slot * Process.pointerSize).writePointer(node);
      slotLimit.writeU32(Math.max(slotLimit.readU32(), slot + 1));
      entries.set(node.toString(), row);
      pending.set(node.toString(), row);
      wake();
    },
    restore(stop = false) { stopped = stop; restoring = true; wake(); },
    status() { return {compacted: entries.size, restoring, applied, layoutMs}; }
  };
}
