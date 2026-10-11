# Diarion

A private diary and life tracker for iOS and Android: daily entries, mood, sleep, habits,
planner, notes, finances, reading and an optional cycle calendar. No account, no server, no
analytics — everything stays in an encrypted database on the phone.

- App Store: <https://apps.apple.com/ua/app/diarion/id6814251806>
- Website and privacy policy: <https://diarion.org>

## Why the code is public

So that the privacy claim can be checked rather than taken on trust. The code is visible, not
open source: see [LICENSE](LICENSE). It may be read and forked on GitHub, but not copied,
rebuilt or published.

## Stack

.NET 10 MAUI, strict MVVM with CommunityToolkit.Mvvm, LiteDB (encrypted, local only), xUnit.
`Diarion.Core` holds the domain and view models with no UI dependency; `Diarion` is the MAUI
head; `Diarion.Tests` runs on any OS. Product decisions live in
[PRODUCT_ROADMAP.md](PRODUCT_ROADMAP.md) and [specs/](specs/README.md).

## Contact

support@diarion.org
