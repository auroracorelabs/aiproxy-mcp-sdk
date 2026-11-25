import {readFile,writeFile,mkdir,rm,cp,access,readdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Marked} from 'marked';
const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../..'),dist=path.join(root,'dist/docs');
const base=process.env.DOCS_BASE_PATH ?? '/aiproxy-mcp-sdk/';
if(!/^\/(?:[A-Za-z0-9._-]+\/)*$/.test(base))throw new Error('DOCS_BASE_PATH must be a slash-delimited URL path');
const origin=process.env.DOCS_ORIGIN ?? 'https://auroracorelabs.github.io';
const repo='https://github.com/auroracorelabs/aiproxy-mcp-sdk';
const version=(await readFile(path.join(root,'VERSION'),'utf8')).trim();
const esc=(s: string)=>s.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
const url=(route: string)=>base+route.replace(/^\//,'');
const entries=[
 ['','docs/site/content/index.md','Start'],
 ['getting-started/','docs/site/content/getting-started.md','Start'],
 ['dotnet/','dotnet/AiProxy.Mcp.Sdk/README.md','Language guides'],
 ['rust/','rust/README.md','Language guides'],
 ['message-signed/','docs/MESSAGE_SIGNED.md','Reference'],
 ['charts/','docs/CHARTS.md','Reference'],
 ['releasing/','docs/RELEASING.md','Contributing'],
 ['documentation/','docs/PAGES.md','Contributing'],
];
type Page={route:string,source:string,group:string,text:string,title?:string};
const pages:Page[]=await Promise.all(entries.map(async([route,source,group])=>({route,source,group,text:await readFile(path.join(root,source),'utf8')})));
const aspNetCoreSource='dotnet/AiProxy.Mcp.Sdk.AspNetCore/README.md';
const dotnetPage=pages.find(p=>p.route==='dotnet/');
if(!dotnetPage)throw new Error('Missing dotnet page');
dotnetPage.text=dotnetPage.text.trimEnd()+'\n\n## ASP.NET Core middleware\n\n'+(await readFile(path.join(root,aspNetCoreSource),'utf8')).replace(/^# .+\n\n/, '');
const navigationTitles: Record<string,string>={
 '':'Overview','getting-started/':'Getting started',
 'dotnet/':'.NET','rust/':'Rust',
 'message-signed/':'message_signed contract','charts/':'Charts',
 'releasing/':'Build and release','documentation/':'Documentation maintenance',
};
for(const p of pages)p.title=p.text.match(/^# (.+)$/m)?.[1]??p.route;
const references=[
 ['.NET API','reference/dotnet/index.html'],
 ['Rust API','reference/rust/aiproxy_mcp_sdk/index.html'],
];
function resolveLink(href: string,page: Page){
 if(/^(?:https?:|mailto:|data:|#)/.test(href))return href;
 if(href.startsWith('/'))return url(href);
 const hash=href.indexOf('#'),fragment=hash>=0?href.slice(hash):'',file=hash>=0?href.slice(0,hash):href;
 const source=path.posix.normalize(path.posix.join(path.posix.dirname(page.source),file));
 const target=pages.find(p=>p.source===source
  ||(p.route==='dotnet/'&&source===aspNetCoreSource)
  ||(p.route==='getting-started/'&&source==='README.md')
  ||(p.route==='message-signed/'&&(source==='docs/MESSAGE_SIGNED.md'||source==='MESSAGE_SIGNED.md')));
 return target?url(target.route)+(fragment||(source===aspNetCoreSource?'#aspnet-core-middleware':'')):repo+'/blob/main/'+source+fragment;
}
await rm(dist,{recursive:true,force:true});await mkdir(dist,{recursive:true});
await cp(path.join(here,'public'),dist,{recursive:true});
await mkdir(path.join(dist,'reference'),{recursive:true});
await cp(path.join(root,'build/docs-api','dotnet'),path.join(dist,'reference','dotnet'),{recursive:true});
await cp(path.join(root,'build/docs-rust/doc'),path.join(dist,'reference/rust'),{recursive:true});
// DocFX's build manifest contains workstation paths and is not a serving asset.
await rm(path.join(dist,'reference/dotnet/manifest.json'),{force:true});
async function adjustReferenceLinks(directory: string): Promise<void> {
 for(const entry of await readdir(directory,{withFileTypes:true})) {
  const file=path.join(directory,entry.name);
  if(entry.isDirectory())await adjustReferenceLinks(file);
  else if(file.endsWith('.html')) {
   let html=await readFile(file,'utf8');
   html=html.replace(/href="\.\.\/\.\.\/index\.html"([^>]*>SDK guides)/g,'href="'+base+'"$1');
   html=html.replaceAll('href="https://auroracorelabs.github.io/aiproxy-mcp-sdk/"','href="'+base+'"');
   await writeFile(file,html);
  }
 }
}
await adjustReferenceLinks(path.join(dist,'reference'));
for(const [,file] of references)await access(path.join(dist,file));
for(const page of pages){
 const toc: {depth:number,text:string,id:string}[]=[],counts=new Map<string,number>();
 const markdown=new Marked({gfm:true,renderer:{
  link({href,title,tokens}){return '<a href="'+esc(resolveLink(href,page))+'"'+(title?' title="'+esc(title)+'"':'')+'>'+this.parser.parseInline(tokens)+'</a>';},
  heading({tokens,depth}){
   const html=this.parser.parseInline(tokens),text=html.replace(/<[^>]+>/g,'');
   const slug=text.toLowerCase().replace(/&[^;]+;/g,'-').replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,''),n=counts.get(slug)??0;counts.set(slug,n+1);
   const id=slug+(n?'-'+n:'');if(depth>1&&depth<4)toc.push({id,text,depth});
   return '<h'+depth+' id="'+id+'">'+html+'<a class="anchor" href="#'+id+'" aria-label="Link to '+esc(text)+'">#</a></h'+depth+'>';
  },
 }});
 const content=markdown.parse(page.text);
 let nav='';
 for(const group of [...new Set(pages.map(p=>p.group))])nav+='<section><p class="nav-label">'+group+'</p>'+pages.filter(p=>p.group===group).map(p=>'<a href="'+url(p.route)+'"'+(p===page?' aria-current="page"':'')+'>'+esc(navigationTitles[p.route]??p.title)+'</a>').join('')+'</section>';
 nav+='<section><p class="nav-label">Generated API</p>'+references.map(([title,route])=>'<a href="'+url(route)+'">'+title+' ↗</a>').join('')+'</section>';
 const brand='<img src="'+url('assets/favicon.svg')+'" width="36" height="36" alt=""><span>AiProxy <span class="brand-product">MCP SDK</span></span><span class="badge">SDKs</span>';
 const html='<!doctype html><html lang="en" data-base="'+base+'"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="theme-color" content="#0b1f2a"><title>'+esc(page.title??page.route)+' · AiProxy MCP SDK</title><meta name="description" content="AiProxy MCP SDK documentation: message_signed auth, .NET and Rust integration guides, and generated API references."><link rel="canonical" href="'+origin+url(page.route)+'"><link rel="icon" href="'+url('assets/favicon.svg')+'"><link rel="stylesheet" href="'+url('assets/docs.css')+'"><script>try{const t=localStorage.getItem("aiproxy-mcp-sdk-theme");document.documentElement.dataset.theme=t==="light"||t==="dark"?t:matchMedia("(prefers-color-scheme:light)").matches?"light":"dark"}catch{}</script><script src="'+url('assets/docs.js')+'" defer></script></head><body><a class="skip" href="#content">Skip to content</a><header><a class="brand" href="'+base+'">'+brand+'</a><nav aria-label="Primary"><a href="'+repo+'">GitHub ↗</a></nav><a class="download" href="'+repo+'/actions/workflows/ci.yml">Packages</a><button class="menu-toggle" type="button" aria-label="Toggle documentation navigation" aria-controls="documentation-navigation" aria-expanded="false">☰</button><button class="theme" type="button" aria-label="Toggle color theme">◐</button></header><div class="layout"><aside class="sidebar" id="documentation-navigation"><div class="search"><label for="search">Search SDK documentation</label><input id="search" type="search" placeholder="Find a guide…" autocomplete="off"><div id="search-results" role="status" aria-live="polite" hidden></div></div><p class="nav-label">SDK version '+esc(version)+'</p><nav aria-label="Documentation">'+nav+'</nav></aside><main id="content"><div class="eyebrow">'+esc(page.group)+'</div><article>'+content+'</article><footer>AiProxy MCP SDK '+esc(version)+' · <a href="'+repo+'/blob/main/'+page.source+'">View source on GitHub</a></footer></main><aside class="toc"><p class="nav-label">On this page</p>'+toc.map(h=>'<a class="depth-'+h.depth+'" href="#'+h.id+'">'+esc(h.text)+'</a>').join('')+'</aside></div><footer class="site-footer"><div class="footer-grid"><div><a class="brand" href="'+base+'">'+brand+'</a><p>MCP SDK for .NET and Rust: message_signed transport and native plugins.</p></div><nav aria-label="Developer resources"><strong>Developers</strong><a href="'+url('message-signed/')+'">message_signed contract</a><a href="'+repo+'">SDK repository ↗</a></nav><nav aria-label="Packages"><strong>Packages</strong><a href="https://www.nuget.org/packages/AiProxy.Mcp.Sdk">NuGet AiProxy.Mcp.Sdk ↗</a><a href="https://crates.io/crates/aiproxy-mcp-sdk">crates.io aiproxy-mcp-sdk ↗</a></nav></div><div class="copyright">© '+new Date().getFullYear()+' Aurora. All rights reserved.</div></footer></body></html>';
 const out=path.join(dist,page.route);await mkdir(out,{recursive:true});await writeFile(path.join(out,'index.html'),html);
}
await writeFile(path.join(dist,'search.json'),JSON.stringify(pages.map(p=>({title:p.title,url:url(p.route),text:p.text.replace(/\s+/g,' ')}))));
await writeFile(path.join(dist,'.nojekyll'),'');
await writeFile(path.join(dist,'404.html'),'<!doctype html><html lang="en"><meta charset="utf-8"><title>Page not found</title><h1>Page not found</h1><p><a href="'+base+'">AiProxy MCP SDK documentation</a></p></html>');
await writeFile(path.join(dist,'llms.txt'),'# AiProxy MCP SDK\n\n> Official SDK documentation for .NET and Rust MCP servers and plugins.\n\nSource: '+repo+'\nSDK version: '+version+'\n\n## Guides\n\n'+pages.map(p=>'- ['+p.title+']('+origin+url(p.route)+')').join('\n')+'\n\n## Generated API references\n\n'+references.map(([title,route])=>'- ['+title+']('+origin+url(route)+')').join('\n')+'\n');
await writeFile(path.join(dist,'sitemap.xml'),'<?xml version="1.0" encoding="utf-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">'+pages.map(p=>'<url><loc>'+origin+url(p.route)+'</loc></url>').join('')+'</urlset>');
console.log('Built '+pages.length+' SDK guides and generated API references at '+dist+' (base '+base+')');
