# Day-seal browser verification

Run `npm run test:e2e:day-seal` from the frontend workspace. Seven synthetic Chromium scenarios use the actual portal with intercepted BFF responses: reviewed version/CSRF commands, journaled-change invalidation, missing review, accountant read-only controls, malformed proof, uncertain command replay/filter reset and baseline/current/delta comparison. Trace/screenshot/video are disabled. These do not certify real identity or source/broker execution; see [evidence](../../../../docs/Architecture/Evidence/Day-Close-Seals.md).
