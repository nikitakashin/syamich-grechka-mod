#!/usr/bin/env node
// Mechanical sprite-atlas conversion only: crop, alpha-preserving fit, west mirroring.
// AI-authored source atlases remain unchanged under assets/GrechkaDrones/Generation.
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const {Canvas, loadImage} = require(process.env.GRECHKA_SKIA_CANVAS || 'skia-canvas');
const repo = path.resolve(__dirname, '..');
const artRoot = path.join(repo, 'assets/GrechkaDrones');
const frameSize = 256;
const manifest = {version: 1, generator: 'Codex built-in image_gen', frameSize, inputs: {}, outputs: []};
const digest = data => crypto.createHash('sha256').update(data).digest('hex');
const sources = new Map();
async function source(name) {
  if (sources.has(name)) return sources.get(name);
  const filename = path.join(artRoot, 'Generation', name);
  const data = await fs.readFile(filename);
  const image = await loadImage(data);
  const canvas = new Canvas(image.width, image.height);
  const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
  const rgba = context.getImageData(0, 0, image.width, image.height).data;
  const item = {image, rgba, width: image.width, height: image.height};
  manifest.inputs[name] = {sha256: digest(data), width: image.width, height: image.height};
  sources.set(name, item); return item;
}
function gridCuts(src, axis, count) {
  src.cuts ||= {};
  const key = `${axis}:${count}`;
  if (src.cuts[key]) return src.cuts[key];
  const length = axis === 'x' ? src.width : src.height;
  const cross = axis === 'x' ? src.height : src.width;
  const spans = []; let start = -1;
  for (let pos = 0; pos <= length; pos++) {
    let occupied = false;
    if (pos < length) for (let other = 0; other < cross; other++) {
      const x = axis === 'x' ? pos : other, y = axis === 'x' ? other : pos;
      if (src.rgba[(y * src.width + x) * 4 + 3] > 8) { occupied = true; break; }
    }
    if (occupied && start < 0) start = pos;
    if (!occupied && start >= 0) { if (pos - start >= 16) spans.push([start, pos - 1]); start = -1; }
  }
  if (spans.length !== count) throw Error(`Expected ${count} separated ${axis} sprites, found ${spans.length}`);
  const cuts = [0];
  for (let i = 1; i < spans.length; i++) cuts.push(Math.round((spans[i - 1][1] + spans[i][0]) / 2));
  cuts.push(length); src.cuts[key] = cuts; return cuts;
}
function tileBounds(src, columns, rows, column, row) {
  // Generated sheets have approximate grids. Locate actual transparent gutters, never cut silhouettes.
  const xs = gridCuts(src, 'x', columns), ys = gridCuts(src, 'y', rows);
  const left = xs[column], right = xs[column + 1], top = ys[row], bottom = ys[row + 1];
  let x0 = right, y0 = bottom, x1 = left - 1, y1 = top - 1;
  for (let y = top; y < bottom; y++) for (let x = left; x < right; x++) {
    if (src.rgba[(y * src.width + x) * 4 + 3] > 8) {
      x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y);
    }
  }
  if (x1 < x0) throw Error(`Empty sprite cell ${column},${row}`);
  if (x0 <= left || x1 >= right - 1 || y0 <= top || y1 >= bottom - 1)
    throw Error(`Sprite touches atlas cell boundary ${column},${row}; inspect atlas before conversion`);
  // Two alpha pixels around the visible contour preserve antialiased outlines.
  x0 = Math.max(left, x0 - 2); y0 = Math.max(top, y0 - 2);
  x1 = Math.min(right - 1, x1 + 2); y1 = Math.min(bottom - 1, y1 + 2);
  return {left: x0, top: y0, width: x1 - x0 + 1, height: y1 - y0 + 1};
}
async function emit(role, tier, state, direction, sourceName, sourceRect, src, extent, mirror = false) {
  const canvas = new Canvas(frameSize, frameSize), ctx = canvas.getContext('2d');
  const ratio = extent / Math.max(sourceRect.width, sourceRect.height);
  const width = Math.round(sourceRect.width * ratio), height = Math.round(sourceRect.height * ratio);
  ctx.imageSmoothingEnabled = true;
  ctx.save();
  if (mirror) { ctx.translate(frameSize, 0); ctx.scale(-1, 1); }
  ctx.drawImage(src.image, sourceRect.left, sourceRect.top, sourceRect.width, sourceRect.height,
    Math.round((frameSize - width) / 2), Math.round((frameSize - height) / 2), width, height);
  ctx.restore();
  const prefix = state === 'Unit' ? `Drones/${role}/T${tier}/${role}T${tier}` : `Docks/${role}/T${tier}/${role}T${tier}_${state}`;
  const relative = `${prefix}_${direction}.png`, output = path.join(artRoot, 'Textures', relative);
  const buffer = await canvas.toBuffer('png');
  await fs.mkdir(path.dirname(output), {recursive: true}); await fs.writeFile(output, buffer);
  manifest.outputs.push({path: `Textures/${relative}`, sha256: digest(buffer), source: sourceName,
    sourceRect, extent, mirror, role, tier, state, direction});
}
(async () => {
  for (const role of ['Rescue', 'Warrior']) {
    const unitName = `${role.toLowerCase()}-units-v1.png`, dockName = `${role.toLowerCase()}-docks-v1.png`;
    const units = await source(unitName), docks = await source(dockName);
    for (let tier = 1; tier <= 5; tier++) {
      for (const [index, direction] of ['south', 'east', 'north'].entries()) {
        const unit = tileBounds(units, 3, 5, index, tier - 1);
        await emit(role, tier, 'Unit', direction, unitName, unit, units, role === 'Rescue' ? 136 : 144);
        if (direction === 'east') await emit(role, tier, 'Unit', 'west', unitName, unit, units, role === 'Rescue' ? 136 : 144, true);
        for (const [state, offset] of [['Empty', 0], ['Occupied', 3]]) {
          const dock = tileBounds(docks, 6, 5, index + offset, tier - 1);
          // The generated rescue empty-east socket faces right. Normalize it to the east-facing robot's rear/left.
          const reverse = role === 'Rescue' && state === 'Empty' && direction === 'east';
          await emit(role, tier, state, direction, dockName, dock, docks, 232, reverse);
          if (direction === 'east') await emit(role, tier, state, 'west', dockName, dock, docks, 232, !reverse);
        }
      }
    }
  }
  await fs.writeFile(path.join(artRoot, 'art-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  // Preview is built from the final exact game PNGs, with a separate readable display background.
  const preview = new Canvas(1250, 850), ctx = preview.getContext('2d');
  ctx.fillStyle = '#22272a'; ctx.fillRect(0, 0, 1250, 850);
  ctx.fillStyle = '#f1f0e9'; ctx.font = 'bold 25px Arial'; ctx.fillText('GrechkaDrones', 32, 37);
  const labels = ['T1', 'T2', 'T3', 'T4', 'T5'];
  for (let col = 0; col < 5; col++) { ctx.font = 'bold 20px Arial'; ctx.fillText(labels[col], 195 + col * 210, 73); }
  for (const [row, item] of [['Rescue', 'Unit'], ['Warrior', 'Unit'], ['Rescue', 'Occupied'], ['Warrior', 'Empty']].entries()) {
    ctx.font = '16px Arial'; ctx.fillStyle = '#f1f0e9';
    ctx.fillText(['МЧСник', 'Воин', 'Док занят', 'Док пуст'][row], 20, 170 + row * 180);
    const [role, state] = item;
    for (let tier = 1; tier <= 5; tier++) {
      const prefix = state === 'Unit' ? `Drones/${role}/T${tier}/${role}T${tier}` : `Docks/${role}/T${tier}/${role}T${tier}_${state}`;
      const im = await loadImage(path.join(artRoot, 'Textures', prefix + '_south.png'));
      ctx.drawImage(im, 125 + (tier - 1) * 210, 80 + row * 180, 200, 200);
    }
  }
  await preview.toFile(path.join(artRoot, 'preview-v1.png'));
  if (process.argv.includes('--apply')) {
    for (const item of manifest.outputs) {
      const target = path.join(repo, 'mods/GrechkaDrones', item.path);
      await fs.mkdir(path.dirname(target), {recursive: true});
      await fs.copyFile(path.join(artRoot, item.path), target);
    }
    console.log('Installed only the GrechkaDrones manifest PNGs into the mod.');
  }
  console.log(`Prepared ${manifest.outputs.length} original RGBA sprites; originals preserved; manifest and preview saved.`);
})().catch(error => { console.error(error); process.exitCode = 1; });
