import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

// Unmount between tests so a rendered page from one test can never satisfy the next one's query.
afterEach(() => cleanup());
