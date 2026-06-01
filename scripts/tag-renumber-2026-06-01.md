# Tag Renumber — 2026-06-01

## What happened

All 43 local git tags were **renumbered for consistency** (close version gaps,
remove the 4-part `v3.9.85` oddity, and the patch-level `v4.0.1 -> v5.0.2` jump).

- **Scheme chosen:** *Keep the four majors (2 / 3 / 4 / 5) as real milestones;
  renumber each major's minor/patch to be gapless while preserving which releases
  were patches vs. new minors.*
- **Resulting latest version:** `v5.0.0` (the old `v5.0.2`).
- **Operation was LOCAL ONLY.** Nothing was pushed. Commits/content were never
  touched — only tag refs (pointers) were moved.

### Remote divergence warning

At the time of renumber, `origin` had exactly one tag: `v3.0.2` -> commit
`19e93d846c9f2680c09199522b1ce3b2e4977036`. That commit is now the LOCAL `v3.0.0`.
The new LOCAL `v3.0.2` points at a DIFFERENT commit (`6d09812...`, old `v3.0.4`).
So the name `v3.0.2` means two different commits local vs. remote. A plain
`git push --tags` will be REJECTED for `v3.0.2`; syncing requires force.

## Mapping (old name -> new name -> commit SHA)

Each line: `OLD_TAG  NEW_TAG  COMMIT_SHA`. The new tag now points at COMMIT_SHA.
Note: old `v2.7.11`/`v2.7.12` share commit `fefc8196...`; old `v3.0.2`/`v3.0.3`
share commit `19e93d84...` (preserved as two new tags on one commit).

```
v2.1.0   v2.0.0   8653e7d96e72c588ea43b0107c2a13e51fbe9759
v2.2.0   v2.1.0   a440d515a20ad06df4cfe7d4ff3d9bd1330ed84f
v2.2.1   v2.1.1   0e2234c9ef62a46cb9ea6ac895f3a21f695735e6
v2.5.0   v2.2.0   baec1761051014dd7f05e672c2a09ba595e00e63
v2.6.0   v2.3.0   bff61afafbd7c57cdc4b2a9f22c8045af7068b69
v2.7.5   v2.4.0   b38b93e9b9a75a993f9fd625e883564449b4ee44
v2.7.7   v2.4.1   5f349e6ede6a7b483de61e5c325e1a312968ceb9
v2.7.11  v2.4.2   fefc81960e077e1b6f95996da08c5e321e321e6e
v2.7.12  v2.4.3   fefc81960e077e1b6f95996da08c5e321e321e6e
v2.7.13  v2.4.4   cb123779dda7bb564facc5fdc295695441083d77
v2.7.15  v2.4.5   f8ea8ff7cd2f1f10e5264d7676d4697300fdc4c8
v2.7.19  v2.4.6   715c0a550652441183c034f0914dfc43728fa480
v2.7.22  v2.4.7   b4f7dda7220ed055017af562fcc864b2a46bd3de
v2.7.23  v2.4.8   7e4137bad359add402acd0883d9e28c516434032
v2.7.24  v2.4.9   5ac6141f41e10fef327fc75872a7a581220f107d
v2.7.28  v2.4.10  19de158620575df33202f4d32ad4d652820e8ba0
v2.8.2   v2.5.0   eea77fb49eded222446642231c44dd2e5a078f68
v2.8.3   v2.5.1   aa9bd12892fbce4b1f83d385648baf33bdb658d5
v2.8.4   v2.5.2   913e606217a875f965c1024ae48857657502e5de
v2.9.5   v2.6.0   0edaeee1b77e5131c285582e206b4b0ae6fc5068
v2.10.0  v2.7.0   dae874cdfc48211cf682c30ee61ffda7be5d91a4
v3.0.2   v3.0.0   19e93d846c9f2680c09199522b1ce3b2e4977036
v3.0.3   v3.0.1   19e93d846c9f2680c09199522b1ce3b2e4977036
v3.0.4   v3.0.2   6d09812cec51f1e681172989a31821d4ede6d384
v3.1.0   v3.1.0   73295f86311fdfb07b45421e3af33432c14d9cb9
v3.1.2   v3.1.1   99f7a99db08c385c85aec025352e92bba76d44ff
v3.1.3   v3.1.2   bd28afa332b6a6ed06bc38cfa801b260c1e084f4
v3.1.4   v3.1.3   d7af173b17a136c1e10916ecc198c39228c79650
v3.1.5   v3.1.4   87e9a60bf898832390b37567af69263aca1de5f2
v3.6.0   v3.2.0   dbff8058751c60fa0deff8b5dc614e3ca350e030
v3.6.5   v3.2.1   46ee8c274fd0f2a7d3a394198f88b1f58242f3ac
v3.7.0   v3.3.0   baefe481b0a690308c00711b5a1d096572fa1fe7
v3.7.5   v3.3.1   bdff2ef9c1dd915b245a84df266246ab0d74524f
v3.8.0   v3.4.0   225a8f6806d1cd29d36f0345460d9a07aa19e299
v3.9.0   v3.5.0   195aacc579850f1abe3458bd4e9e097f48bc714a
v3.9.5   v3.5.1   b918a543f20fcc0b650432fee3a5c73361e9be7a
v3.9.6   v3.5.2   9531035143b3c425b8a7bd62bad16f4586998007
v3.9.8   v3.5.3   2b3d298b4ed604650c525aea464bfef68df9218c
v3.9.85  v3.5.4   c7dd40a8ebf7e9d708202a5f7e683f9bb4a2c8af
v3.9.9   v3.5.5   b463703fa9ba4f627898aaee2978411eea0a2bb1
v4.0.0   v4.0.0   d7300b07c7a6c01335cd45dea2c4419032dcea1f
v4.0.1   v4.0.1   59fc7b35d23b93fa670399892ba15275d6249467
v5.0.2   v5.0.0   ad2a4e4f6b521f38d7c0ae99f91c15884a9c9e5e
```

## How to restore the OLD tag names (full rollback)

Run from the repo root. This deletes the new tags and recreates the old ones at
the same commits (columns 1 and 3 of the mapping above):

```bash
# Rollback: recreate OLD tags, remove NEW tags.
# Paste the mapping block above into /tmp/map.txt first (old new sha per line).
while read -r old new sha; do
  [ -z "$old" ] && continue
  git tag -d "$new" 2>/dev/null
  git tag -a "$old" "$sha" -m "Release $old"
done < /tmp/map.txt
```

To restore only a single old tag, e.g. `v5.0.2`:

```bash
git tag -d v5.0.0
git tag -a v5.0.2 ad2a4e4f6b521f38d7c0ae99f91c15884a9c9e5e -m "Release v5.0.2"
```

All listed commit SHAs remain reachable through branch history, so restoration is
always possible regardless of the current tag state.
