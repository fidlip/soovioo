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
5. Open the pull request against `main` and explain what you changed and why.

Pull requests are merged with "Rebase and merge" only. Every commit of `main` is
deployed to the test instance (`test.soovioo.cloud`), where it's tried out.
Production gets it only in a release: a maintainer picks a tested commit of
`main` and publishes a release `vRR.M.N` (year.month.order in the month), which
deploys that commit's images, already built and tested, to production.

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
