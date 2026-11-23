# Contributing

Thanks for your interest in Soovioo! Bug reports, ideas and pull requests are welcome.
Issues and pull requests may be written in English or Czech.

## Bugs and ideas

Open a [GitHub issue](https://github.com/fidlip/soovioo/issues). For a bug, describe what
you did, what you expected and what happened instead. **Do not report security issues
publicly.** See [SECURITY.md](SECURITY.md).

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Set up the development environment following [README.md](README.md#rychlé-spuštění-dev).
3. Run the tests before submitting (see [Testy](README.md#testy)). CI must pass.
4. Write commit messages in English: short and to the point.
5. Open the pull request against `test` and explain what you changed and why.

Changes reach production in two steps, both through pull requests merged with
"Rebase and merge" only:

- **Branch → `test`.** Once merged, CI deploys `test` to the test instance
  (`test.soovioo.cloud`), where the change is tried out.
- **`test` → `main`.** A maintainer promotes everything in `test` at once; CI
  deploys `main` to production. Afterwards CI resets `test` to `main` (same
  content, new commits from the rebase), so the next change starts from a clean
  history. If `test` already holds something newer, the reset is skipped and
  `test` is rebased onto `main` by hand before the next promotion.

If `main` gets a change that didn't go through `test` (an urgent fix), `test` is
rebased onto `main` before its next promotion.

## Contributor License Agreement (CLA)

Before your pull request can be merged, you must sign the [CLA](CLA.md). You only do this
once, on your first pull request: the CLA bot asks you to post a confirmation sentence as a
comment, and the signature then covers all your future contributions.

The CLA does not transfer your copyright. It gives the project's author (and their legal
successors) a non-exclusive licence to your contribution, including the right to distribute
the project under a different licence in the future. Without it, the project's licence could
never be changed, because every contributor would have to agree.

If you contribute as part of your job, make sure your employer allows it first.

## License

Soovioo is released under the [European Union Public Licence v. 1.2](LICENSE) (EUPL-1.2).
Your contribution is distributed under the same licence. See the [Licence](README.md#licence)
section of the README for a summary.
