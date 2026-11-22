// ESLint: SonarCloud's JavaScript and TypeScript rules (eslint-plugin-sonarjs, its recommended set) on top of
// typescript-eslint strict, so `npm run lint` shows what SonarCloud would before a push. Everything is an error and
// `--max-warnings 0` backs it up. A deliberate case is disabled where it is, with the reason:
// // eslint-disable-next-line sonarjs/<rule> -- <why>.
import js from "@eslint/js";
import sonarjs from "eslint-plugin-sonarjs";
import tseslint from "typescript-eslint";

const recommended = sonarjs.configs.recommended;
// Each rule the set turns on, as an error (its options kept); the rest stay off
const level = (setting) => (Array.isArray(setting) ? setting[0] : setting);
const asErrors = Object.fromEntries(
  Object.entries(recommended.rules)
    .filter(([, setting]) => level(setting) !== "off" && level(setting) !== 0)
    .map(([rule, setting]) => [rule, Array.isArray(setting) ? ["error", ...setting.slice(1)] : "error"]),
);

export default tseslint.config(
  { ignores: ["dist"] },
  js.configs.recommended,
  ...tseslint.configs.strict,
  {
    plugins: recommended.plugins,
    linterOptions: { reportUnusedDisableDirectives: "error" },
    rules: { ...asErrors, eqeqeq: "error" },
  },
  // The scripts run in Node.
  {
    files: ["scripts/**/*.mjs"],
    languageOptions: { globals: { process: "readonly", console: "readonly", Buffer: "readonly" } },
  },
);
