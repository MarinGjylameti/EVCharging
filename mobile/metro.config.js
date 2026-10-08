const { getDefaultConfig } = require("expo/metro-config");
const { resolve } = require("node:path");
const config = getDefaultConfig(__dirname);
// Only the shared, dependency-free source folders sit outside the app root.
config.watchFolders = [resolve(__dirname, "../shared"), resolve(__dirname, "../contracts")];
module.exports = config;
