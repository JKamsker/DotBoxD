import { readFile } from 'node:fs/promises';
import { extname, resolve } from 'node:path';
import GithubSlugger from 'github-slugger';
import { toString } from 'mdast-util-to-string';
import remarkFrontmatter from 'remark-frontmatter';
import remarkMdx from 'remark-mdx';
import remarkParse from 'remark-parse';
import { unified } from 'unified';

function visit(node, visitor) {
  visitor(node);
  if (!Array.isArray(node.children)) return;
  for (const child of node.children) visit(child, visitor);
}

function addHtmlAnchors(value, anchors) {
  const pattern = /(?:id|name)=["'](?<id>[^"']+)["']/giu;
  for (const match of value.matchAll(pattern)) anchors.add(match.groups.id);
}

function addMdxAnchors(node, anchors) {
  if (node.type !== 'mdxJsxFlowElement' && node.type !== 'mdxJsxTextElement') return;
  for (const attribute of node.attributes ?? []) {
    if ((attribute.name === 'id' || attribute.name === 'name') && typeof attribute.value === 'string') {
      anchors.add(attribute.value);
    }
  }
}

async function extractAnchors(filePath) {
  const source = await readFile(filePath, 'utf8');
  const processor = unified().use(remarkParse).use(remarkFrontmatter);
  if (extname(filePath).toLowerCase() === '.mdx') processor.use(remarkMdx);

  const tree = processor.parse(source);
  const slugger = new GithubSlugger();
  const anchors = new Set();
  visit(tree, (node) => {
    if (node.type === 'heading') anchors.add(slugger.slug(toString(node)));
    if (node.type === 'html') addHtmlAnchors(node.value, anchors);
    addMdxAnchors(node, anchors);
  });
  return [...anchors];
}

async function readStandardInput() {
  process.stdin.setEncoding('utf8');
  let input = '';
  for await (const chunk of process.stdin) input += chunk;
  return input;
}

const result = {};
const commandLinePaths = process.argv.slice(2);
const inputPaths = commandLinePaths.length > 0
  ? commandLinePaths
  : JSON.parse(await readStandardInput());
for (const argument of inputPaths) {
  const filePath = resolve(argument);
  result[filePath] = await extractAnchors(filePath);
}
process.stdout.write(JSON.stringify(result));
