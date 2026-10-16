"use client";

import { createContext, useContext } from "react";

const TestModeContext = createContext(false);

export function RuntimeFeaturesProvider({ testMode, children }: {
  testMode: boolean; children: React.ReactNode;
}) {
  return <TestModeContext.Provider value={testMode}>{children}</TestModeContext.Provider>;
}

export const useTestMode = () => useContext(TestModeContext);
