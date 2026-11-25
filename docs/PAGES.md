# SDK documentation on GitHub Pages

The SDK repository is [auroracorelabs/aiproxy-mcp-sdk](https://github.com/auroracorelabs/aiproxy-mcp-sdk). It owns its SDK guides and generated language API references.

## Publication

The [SDK documentation workflow](https://github.com/auroracorelabs/aiproxy-mcp-sdk/actions/workflows/docs.yml) builds the site on pull requests, pushes to main and manual runs. Pull requests build and validate the Pages artifact. Deployment runs only for the repository's default branch, using the github-pages environment.

In the GitHub repository, open **Settings → Pages → Build and deployment → Source** and select **GitHub Actions**. Then push the prepared documentation changes to main, or run the workflow from that branch. The deployment job reports the actual published URL through its environment output.

For this repository, the expected project-site URL after the first successful deployment is:

https://auroracorelabs.github.io/aiproxy-mcp-sdk/

This URL is not a claim that the site has already been activated. It becomes available after Pages configuration and a successful deployment. No custom domain or gh-pages source branch is required. The workflow uses the built-in GITHUB_TOKEN and Pages OIDC permissions; no additional deployment token is needed.

## Content and API generation

The site separates shared setup, language-specific integration and the wire contract:

- Getting started covers install and first client/server/plugin wiring.
- Language guides reuse package READMEs (the .NET guide merges `AiProxy.Mcp.Sdk` and `AiProxy.Mcp.Sdk.AspNetCore`).
- The message_signed page is `docs/MESSAGE_SIGNED.md`.
- Build and release and this page are contributor docs.

Edit those source files to update their content. Site-specific guides and assets live in `docs/site`.

API references are generated directly from the SDK source:

| Language | Generator | Source |
| --- | --- | --- |
| .NET | DocFX | `AiProxy.Mcp.Sdk` and `AiProxy.Mcp.Sdk.AspNetCore` (net8.0) |
| Rust | rustdoc | The public `aiproxy-mcp-sdk` crate |

Versions of Node dependencies are locked in `docs/site/package-lock.json`. DocFX is pinned in the local tool manifest. Rust dependencies use the crate lockfile.

## Known gaps

| Gap | Notes |
| --- | --- |
| GitHub Pages not auto-enabled | An operator must set Pages source to GitHub Actions once; until then the URL above is expected but inactive. |
| No separate product docs site | This repository publishes SDK guides only (GitHub Pages). There is no separate product/OpenAPI docs domain linked from the site. |
| No `/examples/` page | The repository has no runnable `examples/` tree yet. Add ASP.NET MCP host, signing client and Rust `cdylib` samples before adding an Examples page. |
| Package DocumentationUrl | NuGet/crates metadata still point at the GitHub repository. Optionally point at the Pages URL after the first successful deploy. |

## Debug in VS Code

Open the SDK repository, select **mcp-sdk docs: preview [8092]** under Run and Debug, and press F5. The pre-launch task installs documentation dependencies, generates API references and builds the site. VS Code then starts the preview and opens http://127.0.0.1:8092/aiproxy-mcp-sdk/ in your browser. Stop debugging to stop the server.

After a build, you can also run the preview with `npm run dev --prefix docs/site` (runs `node docs/site/serve.ts`). Source edits require another build or restarting the debug configuration.

## Local build

Install Node.js 24+, .NET SDK 10 and stable Rust. From the repository root:

```sh
npm ci --prefix docs/site
bash docs/site/build-api.sh
npm run build --prefix docs/site
npm test --prefix docs/site
```

The complete static site is written to `dist/docs`. Generated API intermediates are stored under `build`; both directories are ignored. Builds fail if a generated language reference is missing.

The default base path is `/aiproxy-mcp-sdk/`. To preview under that project path, use `npm run dev --prefix docs/site` after a build. Alternatively, build for a root-mounted local server:

```sh
DOCS_BASE_PATH=/ npm run build --prefix docs/site
DOCS_BASE_PATH=/ npm test --prefix docs/site
python3 -m http.server 8092 --directory dist/docs
```

The local preview is then http://localhost:8092/. The workflow sets the production origin and repository base path explicitly.

## Checks

The documentation checks validate the generated language entry points, guide links, anchors, search URLs and Pages base path. API references use their native generators' search and navigation. The guide search covers authored guides. The generated `llms.txt` links both guides and language references for AI discovery.
