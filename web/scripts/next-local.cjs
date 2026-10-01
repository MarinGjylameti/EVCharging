// Avoid telemetry and Next's automatic network repair while dependencies are offline.
process.env.NEXT_TELEMETRY_DISABLED = "1";
process.env.NEXT_IGNORE_INCORRECT_LOCKFILE = "1";
require("next/dist/bin/next");
