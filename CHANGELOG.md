# Changelog

## [1.6.0](https://github.com/Gholie/samklang/compare/v1.5.1...v1.6.0) (2026-07-21)


### Features

* unify format-switch behavior into one mode, start-minimized toggle, and recover Apple Music from post-switch pauses ([#66](https://github.com/Gholie/samklang/issues/66)) ([d20b381](https://github.com/Gholie/samklang/commit/d20b3818c4bd2965c19c2e812fd5512b4609687a))

## [1.5.1](https://github.com/Gholie/samklang/compare/v1.5.0...v1.5.1) (2026-07-09)


### Bug Fixes

* icons were slightly off kilter ([#63](https://github.com/Gholie/samklang/issues/63)) ([879f92e](https://github.com/Gholie/samklang/commit/879f92e571ec83515606ae25a754f0ff5aec1958))

## [1.5.0](https://github.com/Gholie/samklang/compare/v1.4.1...v1.5.0) (2026-07-09)


### Features

* **playback:** play and queue album tracks via the Apple Music app (opt-in) ([#61](https://github.com/Gholie/samklang/issues/61)) ([e5dd646](https://github.com/Gholie/samklang/commit/e5dd646f67ff85f4c8ebc35dfd4d7097ffaebc4f))

## [1.4.1](https://github.com/Gholie/samklang/compare/v1.4.0...v1.4.1) (2026-07-08)


### Bug Fixes

* **ui:** play the clicked album track by catalog deep link, not relative skips ([#59](https://github.com/Gholie/samklang/issues/59)) ([0418e25](https://github.com/Gholie/samklang/commit/0418e25c97f5fc6b08ab7561cd4ee5716c578a31))

## [1.4.0](https://github.com/Gholie/samklang/compare/v1.3.0...v1.4.0) (2026-07-08)


### Features

* **ui:** embed app icon in exe for tray, taskbar and title bar ([#56](https://github.com/Gholie/samklang/issues/56)) ([651bbee](https://github.com/Gholie/samklang/commit/651bbeeac7d85720461caddb083c33e07d582422))


### Bug Fixes

* **ui,logging:** address PR [#54](https://github.com/Gholie/samklang/issues/54) review findings ([#57](https://github.com/Gholie/samklang/issues/57)) ([915b101](https://github.com/Gholie/samklang/commit/915b101e2eea3a384c887907f7b25b067bf1484b))

## [1.3.0](https://github.com/Gholie/samklang/compare/v1.2.3...v1.3.0) (2026-07-08)


### Features

* **logging:** add detailed logging with togglable setting and rolling file rotation ([cabd394](https://github.com/Gholie/samklang/commit/cabd3943cd5bd47e6e4ad11d87bc8459e7055c18))
* **ui:** redesigned dashboard with Apple Music-style track cards and navigation ([#54](https://github.com/Gholie/samklang/issues/54)) ([cabd394](https://github.com/Gholie/samklang/commit/cabd3943cd5bd47e6e4ad11d87bc8459e7055c18))

## [1.2.3](https://github.com/Gholie/samklang/compare/v1.2.2...v1.2.3) (2026-07-08)


### Bug Fixes

* **catalog:** rank matches by album and check same-family editions for quality ([#53](https://github.com/Gholie/samklang/issues/53)) ([20320b6](https://github.com/Gholie/samklang/commit/20320b6855ae117e56b1a34d6eda6045bf28ee9c))
* **sync:** ignore transient SMTC placeholder states between tracks ([#51](https://github.com/Gholie/samklang/issues/51)) ([93d8654](https://github.com/Gholie/samklang/commit/93d86543cbb68fe5c5aae65aaf0efde77e621472))

## [1.2.2](https://github.com/Gholie/samklang/compare/v1.2.1...v1.2.2) (2026-07-08)


### Bug Fixes

* **catalog:** split Apple Music's "Artist — Album" SMTC artist field ([#47](https://github.com/Gholie/samklang/issues/47)) ([e2d0811](https://github.com/Gholie/samklang/commit/e2d0811347ec8d234b41ce58753e0a62d904eef8))
* **tests:** stop dotnet test from polluting the real AppLog file ([#49](https://github.com/Gholie/samklang/issues/49)) ([e506a2f](https://github.com/Gholie/samklang/commit/e506a2ff42926c7a8dd8bdef1e5097e9b875089b))

## [1.2.1](https://github.com/Gholie/samklang/compare/v1.2.0...v1.2.1) (2026-07-08)


### Bug Fixes

* **catalog:** replace permanent session self-disable with bounded backoff, add file logging ([#45](https://github.com/Gholie/samklang/issues/45)) ([9bd9ecf](https://github.com/Gholie/samklang/commit/9bd9ecf6600e5b16ff2d6ef3a09fc020b33b0f58))
* **devices:** report the valid bit depth for extensible Device Formats ([#44](https://github.com/Gholie/samklang/issues/44)) ([ceed8bc](https://github.com/Gholie/samklang/commit/ceed8bcdfe9b4b8ee0471d85ca35cc4c30ee62ba))

## [1.2.0](https://github.com/Gholie/samklang/compare/v1.1.1...v1.2.0) (2026-07-08)


### Features

* **dashboard:** show the current album's tracks instead of the switch log by default ([#43](https://github.com/Gholie/samklang/issues/43)) ([c5b0808](https://github.com/Gholie/samklang/commit/c5b0808a0593480a263b005282eab209006eea61))


### Bug Fixes

* **catalog:** validate scraped developer token by decoding, parse media-group manifests ([#40](https://github.com/Gholie/samklang/issues/40)) ([a56fa5f](https://github.com/Gholie/samklang/commit/a56fa5f24f65ede0aee7c5d743ef184da1cd8671)), closes [#31](https://github.com/Gholie/samklang/issues/31)
* **ui:** restore window chrome with a WPF-UI TitleBar ([#42](https://github.com/Gholie/samklang/issues/42)) ([eeb79d6](https://github.com/Gholie/samklang/commit/eeb79d6c3d75b3a78fd4b962056fad1e75cc8513)), closes [#39](https://github.com/Gholie/samklang/issues/39)

## [1.1.1](https://github.com/Gholie/samklang/compare/v1.1.0...v1.1.1) (2026-07-07)


### Bug Fixes

* **ci:** tag release commit eagerly so release-please detects draft releases ([#34](https://github.com/Gholie/samklang/issues/34)) ([6a4b314](https://github.com/Gholie/samklang/commit/6a4b314ecc00e9643a0a088aae54216915b0c4db)), closes [#32](https://github.com/Gholie/samklang/issues/32)
* **devices:** probe device rates in exclusive mode with the device's own format layout ([#37](https://github.com/Gholie/samklang/issues/37)) ([dd5b7ab](https://github.com/Gholie/samklang/commit/dd5b7ab55b10ca1966330cdf2e57991313916767)), closes [#31](https://github.com/Gholie/samklang/issues/31)
* **now-playing:** stop stale artwork reads from overwriting the current track's artwork ([#33](https://github.com/Gholie/samklang/issues/33)) ([a7de6e7](https://github.com/Gholie/samklang/commit/a7de6e7637f01b31ac89e84c81541701d1e74074)), closes [#30](https://github.com/Gholie/samklang/issues/30)
* **playcache:** recover real codec from encrypted stsd sinf/frma box ([#36](https://github.com/Gholie/samklang/issues/36)) ([0999bfd](https://github.com/Gholie/samklang/commit/0999bfd2ddabf1ef43f70079395b9d7a49bffddb)), closes [#31](https://github.com/Gholie/samklang/issues/31)

## [1.1.0](https://github.com/Gholie/samklang/compare/v1.0.0...v1.1.0) (2026-07-07)


### Features

* modern now-playing dashboard with artwork, controls, and simple-UI toggle ([#26](https://github.com/Gholie/samklang/issues/26)) ([fadfa9f](https://github.com/Gholie/samklang/commit/fadfa9ffe5b4b1428a1d9bf7967c5d0f96f146cb))
* **playback:** Add next-track prefetch buffer for seamless album playback ([#27](https://github.com/Gholie/samklang/issues/27)) ([31b0515](https://github.com/Gholie/samklang/commit/31b0515b8248bc841251527f853cdd041f6feac8))

## 1.0.0 (2026-07-07)


### Features

* **devices:** add device targeting with follow/pin modes ([#16](https://github.com/Gholie/samklang/issues/16)) ([2bd58fc](https://github.com/Gholie/samklang/commit/2bd58fc5608aa5c1fd2fb2f63d9148da29b4a940)), closes [#5](https://github.com/Gholie/samklang/issues/5)
* **devices:** clamp target format to device capabilities ([#13](https://github.com/Gholie/samklang/issues/13)) ([0026c0d](https://github.com/Gholie/samklang/commit/0026c0d9e8adaea2fcc259465506354dfe1b1566))
* **release:** Velopack packaging with release-please-driven, attested releases ([#21](https://github.com/Gholie/samklang/issues/21)) ([330f7f8](https://github.com/Gholie/samklang/commit/330f7f827cff825d532cc67f073f8738963c0e66))
* **resolver:** add catalog layer for exact-confidence resolution ([#17](https://github.com/Gholie/samklang/issues/17)) ([b8fbcc3](https://github.com/Gholie/samklang/commit/b8fbcc3302673d2a97d158c82970c3d116ec35e8))
* **resolver:** add PlayCache layer for offline exact resolution ([#18](https://github.com/Gholie/samklang/issues/18)) ([fe3809c](https://github.com/Gholie/samklang/commit/fe3809cae7143006d505deef8f8e287b1a6fe369))
* **resolver:** tracer bullet — Apple Music track change switches device format ([fc6e426](https://github.com/Gholie/samklang/commit/fc6e42618d961bb77269475f2ef607f75008a3a4))
* **settings:** add settings store and resting-format revert ([#14](https://github.com/Gholie/samklang/issues/14)) ([3ed1684](https://github.com/Gholie/samklang/commit/3ed1684c34efa30f3e60cd90028bf95c59abb3dc))
* **tray:** add tray icon, close-to-tray, pause switching, single-instance guard, and start-with-windows ([#15](https://github.com/Gholie/samklang/issues/15)) ([4249ced](https://github.com/Gholie/samklang/commit/4249cedf4c824a6fa22e66707cd7fb8728622582)), closes [#8](https://github.com/Gholie/samklang/issues/8)
* **ui:** replace placeholder window with Fluent status dashboard ([#19](https://github.com/Gholie/samklang/issues/19)) ([506db0b](https://github.com/Gholie/samklang/commit/506db0b2790273471a69d55540a7c303a3e5004b))


### Bug Fixes

* address correctness bugs and follow-ups from the full code review ([#23](https://github.com/Gholie/samklang/issues/23)) ([ded327d](https://github.com/Gholie/samklang/commit/ded327d32d485bd6b4c9c18d6420fa0e26f7c9ef))
* **playcache:** recognize SubscriptionPlayCache dir, .m4p files, and drms sample entries ([#22](https://github.com/Gholie/samklang/issues/22)) ([85503d8](https://github.com/Gholie/samklang/commit/85503d81420fcba052c971af220934e59a486d4b))
