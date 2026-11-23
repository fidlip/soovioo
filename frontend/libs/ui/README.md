# @fidlip/soovioo-ui

UI shared by the Soovioo app (`apps/web`, from source) and the operator's administration
(installed from GitHub Packages): Markdown and its editor, form helpers, theme, Czech locale for
Optimus UI, notifications and API error messages. Pure formatting (dates, amounts, contacts) is in
the secondary entry point `@fidlip/soovioo-ui/format`, which the mobile app can use too.

The library's CSS is plain (no Tailwind `theme()`, which ng-packagr wouldn't resolve), but its
templates use Tailwind classes: a consumer's Tailwind `content` has to include
`node_modules/@fidlip/soovioo-ui/fesm2022/*.mjs`.

Only generic UI belongs here: nothing that needs the app's services (school, sign-in), nothing of
the operator's logic.

## Release

Bump `version` in `package.json`; on `main` the workflow `UI package` builds it (`nx build ui`) and
publishes it to `https://npm.pkg.github.com`.

Installing it needs a token with `read:packages` and in `.npmrc`:

```
@fidlip:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=${NODE_AUTH_TOKEN}
```
