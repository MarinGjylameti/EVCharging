import { cache } from "react";
import { backend } from "./backend";

export const runtimeFeatures = cache(async (): Promise<{ testMode: boolean }> => {
  try {
    const response = await backend("/features");
    if (!response.ok) return { testMode: false };
    const data = await response.json();
    return { testMode: data.testMode === true };
  } catch {
    // An unavailable or older API must never expose testing tools.
    return { testMode: false };
  }
});
