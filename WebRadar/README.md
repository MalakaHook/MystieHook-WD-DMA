# Local web radar

Started automatically by the desktop application's Radar tab. Requires Node.js 22+, no npm install. Bound exclusively to 127.0.0.1:3080.

The desktop supplies a random per-process publisher token and map folder through environment variables. No license, remote service, or user setup key is involved. The process serves locally installed AppData tiles; see the root README. Existing publisher/session isolation, password protection, input validation, rate limits, and snapshot freshness remain.

Run the local regression test with `node --test test/local.test.mjs`.