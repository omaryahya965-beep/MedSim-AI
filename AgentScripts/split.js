// Split a C# file into one file per top-level type inside its namespace (keeps usings + namespace).
const fs = require('fs'), path = require('path');
const file = process.argv[2]; const src = fs.readFileSync(file, 'utf8');
const nsIdx = src.indexOf('namespace '); const header = src.slice(0, nsIdx);
const nsLine = src.slice(nsIdx, src.indexOf('{', nsIdx) + 1);
const body = src.slice(src.indexOf('{', nsIdx) + 1, src.lastIndexOf('}'));
const lines = body.split('\n'); const chunks = []; let cur = []; let depth = 0;
const strip = s => s.replace(/"(?:[^"\\]|\\.)*"/g, '""').replace(/'(?:[^'\\]|\\.)'/g, "''");
for (const l of lines) {
  cur.push(l);
  for (const ch of strip(l)) { if (ch === '{') depth++; else if (ch === '}') depth--; }
  if (depth === 0 && /^\s{4}\}\s*\r?$/.test(l)) { chunks.push(cur.join('\n')); cur = []; }
}
const out = [];
for (const c of chunks) {
  const m = c.match(/^\s{4}(?:public |internal )?(?:static |abstract |sealed )*(?:class|struct|enum|interface) (\w+)/m);
  if (!m) continue;
  const name = m[1]; const target = path.join(path.dirname(file), name + '.cs');
  fs.writeFileSync(target, header + nsLine + '\n' + c.replace(/^\s*\n/, '') + '\n}\n'); out.push(name);
}
if (out.length < 2 || out.includes(path.basename(file, ".cs"))) { console.log('nothing to split', file); process.exit(0); }
fs.unlinkSync(file); if (fs.existsSync(file + '.meta')) fs.unlinkSync(file + '.meta');
console.log(path.basename(file), '->', out.join(', '));
