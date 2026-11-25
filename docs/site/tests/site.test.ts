import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile,readdir,access,stat} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const dist=fileURLToPath(new URL('../../../dist/docs/',import.meta.url)),base=process.env.DOCS_BASE_PATH??'/aiproxy-mcp-sdk/';
const read=(p: string)=>readFile(path.join(dist,p),'utf8');
async function walk(dir: string): Promise<string[]>{const files: string[]=[];for(const e of await readdir(dir,{withFileTypes:true})){const p=path.join(dir,e.name);if(e.isDirectory())files.push(...await walk(p));else files.push(p);}return files;}
test('all guides and generated language references are present',async()=>{
 for(const file of [
  'index.html',
  'getting-started/index.html',
  'dotnet/index.html',
  'rust/index.html',
  'message-signed/index.html',
  'charts/index.html',
  'releasing/index.html',
  'documentation/index.html',
  'reference/dotnet/index.html',
  'reference/rust/aiproxy_mcp_sdk/index.html',
  '.nojekyll',
  '404.html',
  'sitemap.xml',
 ])await access(path.join(dist,file));
});
test('guide links, fragments and assets resolve under the GitHub Pages repository path',async()=>{
 const files=(await walk(dist)).filter(f=>f.endsWith('.html')&&!f.includes(path.join(dist,'reference')+path.sep));
 for(const file of files){
  const html=await readFile(file,'utf8');
  const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);assert.equal(new Set(ids).size,ids.length,file);
  for(const m of html.matchAll(/(?:href|src)="([^"]+)"/g)){
   if(/^(https?:|mailto:|data:)/.test(m[1]))continue;
   const relative=path.relative(dist,file).replaceAll(path.sep,'/');
   const url=new URL(m[1],'https://example.test'+base+relative);
   assert.ok(url.pathname.startsWith(base),file+': '+m[1]);
   let target=path.join(dist,decodeURIComponent(url.pathname.slice(base.length)));
   if((await stat(target)).isDirectory())target=path.join(target,'index.html');
   await access(target);
   if(url.hash&&target.endsWith('.html'))assert.ok((await readFile(target,'utf8')).includes('id="'+url.hash.slice(1)+'"'),file+': '+m[1]);
  }
 }
});
test('search and AI discovery URLs use the configured project base',async()=>{
 const index=JSON.parse(await read('search.json'));assert.equal(index.length,8);
 for(const p of index){assert.ok(p.url.startsWith(base));assert.ok(p.text.length);}
 const ai=await read('llms.txt');assert.ok(ai.includes('https://github.com/auroracorelabs/aiproxy-mcp-sdk'));
 for(const lang of ['dotnet','rust'])assert.ok(ai.includes(base+'reference/'+lang+'/'));
 assert.ok((await read('assets/docs.js')).includes("dataset.base + 'search.json'"));
 const sitemap=await read('sitemap.xml');
 assert.ok(sitemap.includes(base+'getting-started/'));
 assert.ok(sitemap.includes(base+'message-signed/'));
 assert.ok(sitemap.includes(base+'charts/'));
});
test('.NET reference includes public signing types',async()=>{
 const listing=await walk(path.join(dist,'reference','dotnet'));
 const names=listing.map(f=>path.basename(f));
 assert.ok(names.some(n=>n.includes('McpMessageSigner')),'McpMessageSigner page');
 assert.ok(names.some(n=>n.includes('MessageSignatureVerifier')),'MessageSignatureVerifier page');
});
