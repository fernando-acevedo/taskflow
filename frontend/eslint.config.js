// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const boundaries = require('eslint-plugin-boundaries');

module.exports = defineConfig([
  { ignores: ['src/app/core/api/schema.d.ts'] },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommendedTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      angular.configs.tsRecommended,
    ],
    languageOptions: {
      parserOptions: {
        projectService: { allowDefaultProject: ["playwright.config.ts"], defaultProject: "e2e/tsconfig.json" },
        tsconfigRootDir: __dirname,
      },
    },
    processor: angular.processInlineTemplates,
    rules: {
      // Angular's static validators (Validators.required) are passed unbound by design.
      '@typescript-eslint/unbound-method': 'off',
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    // Frontend module boundaries (see "Frontend architecture" in CLAUDE.md).
    files: ['src/app/**/*.ts'],
    plugins: { boundaries },
    settings: {
      'import/resolver': { typescript: { project: './tsconfig.json' } },
      'boundaries/elements': [
        // `*.routes.ts` files wire a feature's pages and state: a feature's own routes may reach into that feature,
        // and into other features only through their public API (`index.ts`, which exports lazy loaders for pages).
        { type: 'app-routes', pattern: 'src/app/app.routes.ts', mode: 'file' },
        { type: 'feature-routes', pattern: 'src/app/features/*/*.routes.ts', mode: 'file', capture: ['feature'] },
        // A feature's public API: the only file of a feature that other features may import.
        { type: 'feature-api', pattern: 'src/app/features/*/index.ts', mode: 'file', capture: ['feature'] },
        { type: 'feature', pattern: 'src/app/features/*', mode: 'folder', capture: ['feature'] },
        { type: 'core', pattern: 'src/app/core', mode: 'folder' },
        { type: 'shared', pattern: 'src/app/shared', mode: 'folder' },
        { type: 'app', pattern: 'src/app/*.ts', mode: 'file' },
      ],
      'boundaries/ignore': ['**/*.spec.ts'],
    },
    rules: {
      'boundaries/no-unknown': 'off',
      'boundaries/element-types': [
        'error',
        {
          default: 'disallow',
          message: '${file.type} must not import ${dependency.type} (see "Frontend architecture" in CLAUDE.md)',
          rules: [
            { from: 'shared', allow: ['shared'] },
            { from: 'core', allow: ['core', 'shared'] },
            {
              from: 'feature',
              allow: [['feature', { feature: '${from.feature}' }], 'feature-api', 'core', 'shared'],
            },
            { from: 'feature-api', allow: [['feature', { feature: '${from.feature}' }], 'core', 'shared'] },
            {
              from: 'feature-routes',
              allow: [
                ['feature', { feature: '${from.feature}' }],
                ['feature-routes', { feature: '${from.feature}' }],
                'feature-api',
                'core',
                'shared',
              ],
            },
            { from: 'app-routes', allow: ['feature-api', 'core', 'shared', 'app'] },
            { from: 'app', allow: ['app', 'app-routes', 'feature-api', 'core', 'shared'] },
          ],
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
]);
