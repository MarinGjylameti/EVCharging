const { existsSync, readFileSync } = require("node:fs");
const { join } = require("node:path");
exports.linkedProjectId = function linkedProjectId() {
  const path = join(__dirname, "eas-project.json");
  if (!existsSync(path)) return "";
  const id = JSON.parse(readFileSync(path, "utf8")).projectId;
  if (typeof id !== "string" || !/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(id))
    throw new Error("Invalid EVCharging Expo project link.");
  return id;
};
