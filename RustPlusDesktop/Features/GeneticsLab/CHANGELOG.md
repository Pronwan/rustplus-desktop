# Changelog

All notable changes to this project will be documented in this file.

## [1.1.0](https://github.com/JawadYzbk/rust-genetics-lab/releases/tag/v1.1.0) (2026-09-04)

### 🚀 Features

* feat(ci): add automated version bumping, releases, and dist asset packaging ([eaa783e](https://github.com/JawadYzbk/rust-genetics-lab/commit/eaa783eaef1ad9e5c4f45fd955c8a44513e97866))
* feat: desktop capture path with slot extraction and auto-calibration ([b75885e](https://github.com/JawadYzbk/rust-genetics-lab/commit/b75885e2f55a5822fcff616bd22dc7047b92a7eb))
* feat: add NVIDIA Reflex ON + BOOST notice, graphics.reflexmode 2 command, and scanner optimizations ([611cf2e](https://github.com/JawadYzbk/rust-genetics-lab/commit/611cf2ee6154b5aafc4b9110847e9cd63bb082bc))
* feat: read genes with two recognisers and report why a slot fails ([1a88cd2](https://github.com/JawadYzbk/rust-genetics-lab/commit/1a88cd2ad9b49898e40bfc83d655a1d176965f68))
* feat: add manual capture with confirmation, fix detection flicker ([6a009b5](https://github.com/JawadYzbk/rust-genetics-lab/commit/6a009b501303e23292219ccd6649b267af9163b0))
* feat: classify gene glyphs by template instead of running OCR ([7d79adb](https://github.com/JawadYzbk/rust-genetics-lab/commit/7d79adb5c0c7d12fd0c577c7c32d306b153e09d4))
* feat: add phone camera scanner (beta) ([b323614](https://github.com/JawadYzbk/rust-genetics-lab/commit/b3236142a2f8a2302d57704d90e4d2640013487f))
* feat: redesign route inspector workflow ([4a12351](https://github.com/JawadYzbk/rust-genetics-lab/commit/4a12351be85e259c9d23452f849f6bc4891f0a04))
* feat: rebuild farm operations planner ([02f4184](https://github.com/JawadYzbk/rust-genetics-lab/commit/02f4184e0e665478eb4b01e7bd6f99e859157c1b))
* feat: refine genetics workspace and solver ([45b4a79](https://github.com/JawadYzbk/rust-genetics-lab/commit/45b4a795c8bbaa0a8fbf88948663b027224a257e))
* feat(analytics): default anonymous stats on ([2478fa7](https://github.com/JawadYzbk/rust-genetics-lab/commit/2478fa78fb992532912c48476aaf633e26caeb56))
* feat(ui): complete responsive accessibility remediation ([2da6454](https://github.com/JawadYzbk/rust-genetics-lab/commit/2da6454132e35b9278cfde9d091b3fab1b5f00f3))
* feat(genetics): implement 2-level breeding routes sorting and OCR warmup improvements ([918d89c](https://github.com/JawadYzbk/rust-genetics-lab/commit/918d89c1eb97bde343e74ed611b6fe09ba21b123))
* feat(gene-input): auto-advance to next line after 6 genes, wrap overflow ([284f8d7](https://github.com/JawadYzbk/rust-genetics-lab/commit/284f8d7b473cc0ad3838a0e4ac89985849ac0064))
* feat(genetics-lab): match-mode filtering, responsive layout, perf & UX fixes ([e96e047](https://github.com/JawadYzbk/rust-genetics-lab/commit/e96e0476903f6366be48832be93ad9c3cbcb0bcb))
* feat(scanner): rework scan/typing sounds ([a4206bb](https://github.com/JawadYzbk/rust-genetics-lab/commit/a4206bb0f80a07ebfdee0352e9d1d4cf3bf889a8))
* feat(genetics-lab): v1.0.0 — route grouping, alt-plan selection, generation markers, promo slot ([7bb079c](https://github.com/JawadYzbk/rust-genetics-lab/commit/7bb079cb9eba31acc20248b2d1bd4ff685d86ba0))
* feat(genetics-lab): editable target input, smarter presets, tooltips, hover-scroll tabs, light-mode fixes ([9e6635b](https://github.com/JawadYzbk/rust-genetics-lab/commit/9e6635be3effbaf0757def4bb6f0e0014eb201d4))
* feat(deploy): configure Coolify Docker deployment and Nginx SPA hosting ([509568c](https://github.com/JawadYzbk/rust-genetics-lab/commit/509568c823d670418347c2d2c60620115f2fe182))
* feat(scanner): add real-time starvation detection and in-game <= 50 FPS cap warning ([5a117f8](https://github.com/JawadYzbk/rust-genetics-lab/commit/5a117f8e29f1267b4065d52ae9a5ccac96b801fe))
* feat(ui): add GitHub repository and contribute links to header and about modal ([e6197b3](https://github.com/JawadYzbk/rust-genetics-lab/commit/e6197b305c668c0b95a28e8062f1f39f88d9dbf7))
* feat(init): initialize standalone Rust Genetics Lab web application repository ([9dff34c](https://github.com/JawadYzbk/rust-genetics-lab/commit/9dff34c348d7630bfe5430308f06f4c6d096c800))

### 🐛 Bug Fixes

* fix: ignore ink detached from the glyph, and let OCR abstain ([c6dc47f](https://github.com/JawadYzbk/rust-genetics-lab/commit/c6dc47f4b3c4c8359f92d419f2e49ca075283ee9))
* fix: match glyph shape instead of glyph ink ([03e8757](https://github.com/JawadYzbk/rust-genetics-lab/commit/03e8757a5cc832ec47ff09e2276eca986d11cc8d))
* fix: let the recogniser judge legibility, and give capture a target ([8c8d328](https://github.com/JawadYzbk/rust-genetics-lab/commit/8c8d328901164e5f7509582279da1136636df171))
* fix: threshold glyphs with Otsu and show the real OCR input ([a965bec](https://github.com/JawadYzbk/rust-genetics-lab/commit/a965bec7535e5e2b0b1c28fc76a03f3b79f0c584))
* fix: apply the raised distance limit and hold the accepted state ([0caf4dd](https://github.com/JawadYzbk/rust-genetics-lab/commit/0caf4dd1a216f8d8e40d4054a83d06777b0e8c44))
* fix: read genes per slot and stop discarding good samples ([5d934d5](https://github.com/JawadYzbk/rust-genetics-lab/commit/5d934d5d0e6e131b771287e6c67488f0b94542f8))
* fix: unblock camera OCR and tolerate handheld shake ([f12bbdd](https://github.com/JawadYzbk/rust-genetics-lab/commit/f12bbdd607f7a2c88f04eea5683d78897a6506b7))
* fix(scanner): de-duplicate scanned clones and fix scan sound effects ([98952ec](https://github.com/JawadYzbk/rust-genetics-lab/commit/98952ec66238d5bc9864185feca74dcb3b7d0e67))
* fix(theme): improve light mode color contrast and UI tokens across all components ([53cc44c](https://github.com/JawadYzbk/rust-genetics-lab/commit/53cc44c3b226b20571f168ab015c672fa5e1bc4c))

### ⚡ Performance Improvements

* perf(solver): rewrite crossbreeding core around pre-allocation admission test ([ae00a1a](https://github.com/JawadYzbk/rust-genetics-lab/commit/ae00a1a7e774983f7b0d58e7790c20e31d52a246))
* perf(solver): prune unreliable plans and prioritize 100%/low-gen routes ([54b1e18](https://github.com/JawadYzbk/rust-genetics-lab/commit/54b1e18c66b3788ef5891252a9ebcf7afa9d043f))

**Initial Release Tag**: https://github.com/JawadYzbk/rust-genetics-lab/releases/tag/v1.1.0
