// WSL cannot reach the Windows loopback where WebView2 opens its CDP port, so this forwards
// 0.0.0.0:<listen> -> 127.0.0.1:<target>. Run with the WINDOWS node, from the Windows side:
//   node.exe tests\verify\blogapp-cdp-forward.mjs 9334 9333
// then from WSL: CDP_URL=http://<default-route-gateway>:9334. Stop it by PID when done.
import net from 'node:net';
const [listen, target] = [Number(process.argv[2] ?? 9334), Number(process.argv[3] ?? 9333)];
net.createServer((c) => {
  const u = net.connect(target, '127.0.0.1');
  c.pipe(u); u.pipe(c);
  c.on('error', () => u.destroy()); u.on('error', () => c.destroy());
}).listen(listen, '0.0.0.0', () => console.log(`forwarding ${listen} -> ${target}`));
