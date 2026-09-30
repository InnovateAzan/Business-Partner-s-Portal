import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const appDirectory = fileURLToPath(new URL(".", import.meta.url));
const distDirectory = resolve(appDirectory, "dist");
const host = process.env.FRONTEND_HOST || "0.0.0.0";
const port = Number.parseInt(process.env.FRONTEND_PORT || "8088", 10);

const mimeTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".ico": "image/x-icon",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".webp": "image/webp",
  ".woff": "font/woff",
  ".woff2": "font/woff2"
};

if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error("FRONTEND_PORT must be a valid TCP port.");
}

if (!existsSync(distDirectory)) {
  throw new Error("Frontend build not found. Run 'npm run build' before starting production.");
}

function resolveStaticFile(pathname) {
  const requestedPath = normalize(pathname).replace(/^[\\/]+/, "");
  const candidate = resolve(distDirectory, requestedPath);

  return candidate.startsWith(`${distDirectory}\\`) || candidate === distDirectory
    ? candidate
    : null;
}

function sendFile(response, filePath, method) {
  const extension = extname(filePath).toLowerCase();
  const isAsset = filePath !== join(distDirectory, "index.html");

  response.writeHead(200, {
    "Content-Type": mimeTypes[extension] || "application/octet-stream",
    "Cache-Control": isAsset
      ? "public, max-age=31536000, immutable"
      : "no-cache",
    "X-Content-Type-Options": "nosniff"
  });

  if (method === "HEAD") {
    response.end();
    return;
  }

  createReadStream(filePath).pipe(response);
}

createServer((request, response) => {
  if (request.method !== "GET" && request.method !== "HEAD") {
    response.writeHead(405, { Allow: "GET, HEAD" });
    response.end();
    return;
  }

  let pathname;
  try {
    pathname = decodeURIComponent(new URL(request.url || "/", `http://${request.headers.host}`).pathname);
  } catch {
    response.writeHead(400);
    response.end("Invalid request path.");
    return;
  }

  const requestedFile = resolveStaticFile(pathname);
  if (requestedFile && existsSync(requestedFile) && statSync(requestedFile).isFile()) {
    sendFile(response, requestedFile, request.method);
    return;
  }

  // Client-side routes (for example /set-password) must receive the React entry point.
  // Requests that look like missing static files remain 404 responses.
  if (!extname(pathname)) {
    sendFile(response, join(distDirectory, "index.html"), request.method);
    return;
  }

  response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
  response.end("Not found.");
}).listen(port, host, () => {
  console.log(`Business Partner Portal frontend is listening on http://${host}:${port}`);
});
