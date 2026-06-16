function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function renderInline(text) {
  let s = escapeHtml(text);

  s = s.replace(/`([^`\n]+)`/g, '<code>$1</code>');
  s = s.replace(/\*\*([^*\n]+)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/__([^_\n]+)__/g, '<strong>$1</strong>');
  s = s.replace(/(^|[^*])\*([^*\n]+)\*(?!\*)/g, '$1<em>$2</em>');
  s = s.replace(/(^|[^_])_([^_\n]+)_(?!_)/g, '$1<em>$2</em>');
  s = s.replace(/\[([^\]]+)\]\(((?:https?:\/\/|mailto:)[^\s)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');

  return s;
}

function isListItemStart(line) {
  return /^[-*+]\s+/.test(line) || /^\d+\.\s+/.test(line);
}

function parseTableRow(line) {
  let inner = line.trim();
  if (inner.startsWith('|')) inner = inner.slice(1);
  if (inner.endsWith('|')) inner = inner.slice(0, -1);
  return inner.split('|').map(cell => cell.trim());
}

function parseTableSeparator(line) {
  const cells = parseTableRow(line);
  if (!cells.length) return null;
  const aligns = [];
  for (const cell of cells) {
    if (!/^:?-{3,}:?$/.test(cell)) return null;
    const left = cell.startsWith(':');
    const right = cell.endsWith(':');
    if (left && right) aligns.push('center');
    else if (right) aligns.push('right');
    else aligns.push('left');
  }
  return aligns;
}

export function renderMarkdown(md) {
  if (md == null) return '';
  const text = String(md).replace(/\r\n?/g, '\n');
  const lines = text.split('\n');

  const out = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    if (!line.trim()) { i++; continue; }

    if (/^```/.test(line)) {
      const lang = line.replace(/^```\s*/, '').trim();
      const codeLines = [];
      i++;
      while (i < lines.length && !/^```/.test(lines[i])) {
        codeLines.push(lines[i]);
        i++;
      }
      if (i < lines.length) i++;
      const langAttr = lang ? ` data-lang="${escapeHtml(lang)}"` : '';
      out.push(`<pre${langAttr}><code>${escapeHtml(codeLines.join('\n'))}</code></pre>`);
      continue;
    }

    if (/^\s*([-*_])\s*\1\s*\1[\s\S]*$/.test(line) && /^([-*_])\s*\1\s*\1\s*$/.test(line)) {
      out.push('<hr>');
      i++;
      continue;
    }

    const heading = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
    if (heading) {
      const level = heading[1].length;
      out.push(`<h${level}>${renderInline(heading[2])}</h${level}>`);
      i++;
      continue;
    }

    if (/^>\s?/.test(line)) {
      const quoteLines = [];
      while (i < lines.length && /^>\s?/.test(lines[i])) {
        quoteLines.push(lines[i].replace(/^>\s?/, ''));
        i++;
      }
      out.push(`<blockquote>${renderInline(quoteLines.join('<br>'))}</blockquote>`);
      continue;
    }

    if (/^[-*+]\s+/.test(line)) {
      const items = [];
      while (i < lines.length && /^[-*+]\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^[-*+]\s+/, ''));
        i++;
      }
      out.push('<ul>' + items.map(it => `<li>${renderInline(it)}</li>`).join('') + '</ul>');
      continue;
    }

    if (/^\d+\.\s+/.test(line)) {
      const items = [];
      while (i < lines.length && /^\d+\.\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\d+\.\s+/, ''));
        i++;
      }
      out.push('<ol>' + items.map(it => `<li>${renderInline(it)}</li>`).join('') + '</ol>');
      continue;
    }

    if (line.includes('|') && i + 1 < lines.length) {
      const aligns = parseTableSeparator(lines[i + 1]);
      if (aligns) {
        const headers = parseTableRow(line);
        if (headers.length === aligns.length) {
          const rows = [];
          i += 2;
          while (i < lines.length && lines[i].trim() && lines[i].includes('|')) {
            const cells = parseTableRow(lines[i]);
            if (cells.length !== headers.length) break;
            rows.push(cells);
            i++;
          }
          const ths = headers.map((h, idx) =>
            `<th style="text-align:${aligns[idx]}">${renderInline(h)}</th>`
          ).join('');
          const trs = rows.map(r => '<tr>' + r.map((c, idx) =>
            `<td style="text-align:${aligns[idx]}">${renderInline(c)}</td>`
          ).join('') + '</tr>').join('');
          out.push(`<table class="md-table"><thead><tr>${ths}</tr></thead><tbody>${trs}</tbody></table>`);
          continue;
        }
      }
    }

    const paraLines = [];
    while (i < lines.length) {
      const cur = lines[i];
      if (!cur.trim()) break;
      if (/^```/.test(cur)) break;
      if (/^([-*_])\s*\1\s*\1\s*$/.test(cur)) break;
      if (/^#{1,6}\s+/.test(cur)) break;
      if (/^>\s?/.test(cur)) break;
      if (isListItemStart(cur)) break;
      if (cur.includes('|') && i + 1 < lines.length && parseTableSeparator(lines[i + 1])) break;
      paraLines.push(cur);
      i++;
    }
    if (paraLines.length) {
      const renderedLines = paraLines.map(l => renderInline(l)).join('<br>');
      out.push(`<p>${renderedLines}</p>`);
    }
  }

  return out.join('\n');
}
