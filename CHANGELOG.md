# 1.0.0 (2026-06-30)


### Bug Fixes

* **awareness:** detect WhatsApp's field in its separate top-level WebView2 window ([211cd4a](https://github.com/mahdiahmadi1991/ai-translator/commit/211cd4a86b5d7a9fc03fc67b87b4323cfa0e3802))
* **awareness:** detect WhatsApp/Telegram correctly (regex monikers + WebView2) ([a485870](https://github.com/mahdiahmadi1991/ai-translator/commit/a4858703cfa44c0e07e0fe8d1f5af73dfc58bb04))
* **awareness:** non-blocking WebView2 resolve with Pending status + self-driven retry ([d72c47d](https://github.com/mahdiahmadi1991/ai-translator/commit/d72c47db8d91e2166397853d9f968ffcb366fbf2))
* **awareness:** stop false badges from sticky DOM focus in multi-window Chromium ([3770964](https://github.com/mahdiahmadi1991/ai-translator/commit/377096450480e721e718950e76340fbf2afc6175))
* **awareness:** wake WebView2 a11y so the badge appears in WhatsApp; auto-close badge menu ([6358b1b](https://github.com/mahdiahmadi1991/ai-translator/commit/6358b1b7052b038866388d7cd9f81d37010d092a))
* **build:** force single restore source via RestoreSources (VS-over-WSL) ([d6b0f2e](https://github.com/mahdiahmadi1991/ai-translator/commit/d6b0f2ea62b6429c2573d1321b2b62504cd8042f))
* **build:** green first Windows build for Infrastructure/App ([7682489](https://github.com/mahdiahmadi1991/ai-translator/commit/7682489dbb2e304536175e819ecc0c0e72d35142))
* **build:** offline NuGet package cache for network-blocked Windows ([cec5458](https://github.com/mahdiahmadi1991/ai-translator/commit/cec545866e4a24d43600524fb3ddd95cbbe7fb6f))
* **build:** Windows restore — verified package versions + source mapping ([af4154d](https://github.com/mahdiahmadi1991/ai-translator/commit/af4154ddb3cb4a8581c0b3e2b2b183a09e26844c))
* **inject:** always paste via clipboard (fixes invisible text in Chromium editors) ([1d301ee](https://github.com/mahdiahmadi1991/ai-translator/commit/1d301ee4bfd0bdab6fcaa1af94568bad39583ac9))
* **inject:** append translation (preserve existing text); caret to end ([d1fd71f](https://github.com/mahdiahmadi1991/ai-translator/commit/d1fd71f9af2f4c74e7661dca28b6dddb7be04282))
* **inject:** verify SetValue actually took; clipboard fallback for Chromium ([8d5f1ea](https://github.com/mahdiahmadi1991/ai-translator/commit/8d5f1ea9f998776e2fa365bbb5dfb3ae044d0890))
* **m2:** resolve 10 confirmed findings from adversarial review ([db1f004](https://github.com/mahdiahmadi1991/ai-translator/commit/db1f004841017484a0f17d9720e5afa162b01c1f))
* **overlay:** box no longer closes itself during translation ([2a5ad69](https://github.com/mahdiahmadi1991/ai-translator/commit/2a5ad69b744f69206ad8451b629049d3bf856ea4))
* **overlay:** clear+hide after successful translate; reliable auto-hide ([5ea494c](https://github.com/mahdiahmadi1991/ai-translator/commit/5ea494c297c9f3075a3d65153b4674757d4dfeb1))
* **overlay:** no crash on typing + keep the box on-screen ([0bcf88a](https://github.com/mahdiahmadi1991/ai-translator/commit/0bcf88a2c6d6da3f55ef8168a13a89bc78623892))
* **overlay:** solid background + immediate processing spinner ([bf5b334](https://github.com/mahdiahmadi1991/ai-translator/commit/bf5b334ac494f7dc87baad18e758f690385894f9))
* **release:** pin the first release to 1.0.0 via fallback-version ([5f7a8bd](https://github.com/mahdiahmadi1991/ai-translator/commit/5f7a8bd49ed72c83c037297fd4978d3a64bf67fe))
* **settings:** crash opening Settings — toggle switch storyboard name scope ([b7ce0c9](https://github.com/mahdiahmadi1991/ai-translator/commit/b7ce0c94f6cc3246e089399efb2158acc47d3281))
* **ux:** professional Settings window + auto-dismiss overlay + on-screen box ([6a93c4b](https://github.com/mahdiahmadi1991/ai-translator/commit/6a93c4bb728abf72e582db3aa6c5c17b6905b47b))


### Features

* **app:** non-activating BadgeWindow for M2 awareness (Task 4) ([162a127](https://github.com/mahdiahmadi1991/ai-translator/commit/162a1273f42e9effff663dd53047fd7f3b2634b1))
* **app:** settings editor for awareness + docs sync (M2 Task 7) ([d96880f](https://github.com/mahdiahmadi1991/ai-translator/commit/d96880fc61044ae8cbd91b1eb40736ebd5e93bac))
* **app:** wire M2 awareness — badge appears + targets the focused field (Task 5) ([65674e6](https://github.com/mahdiahmadi1991/ai-translator/commit/65674e673aecfee8b889d21b7e65746efa3451d9))
* **awareness:** opt-out activation — badge everywhere except a blocklist ([441f0f2](https://github.com/mahdiahmadi1991/ai-translator/commit/441f0f20dd7cd0504ba20e2dc5ecadd6ac0fb15e))
* **branding:** app icon + Fluent icons on actions; exe metadata ([4361026](https://github.com/mahdiahmadi1991/ai-translator/commit/43610269a2226a2e077d3c8a2ce0a379b2fb33ea))
* **core:** IFocusWatcher abstraction for M2 awareness (Task 2 step 1) ([fbc31f2](https://github.com/mahdiahmadi1991/ai-translator/commit/fbc31f23303cdb2531d33f1b6ab9d1a20498e89b))
* **core:** per-app badge offset resolution (M2 Task 6) ([2b4905f](https://github.com/mahdiahmadi1991/ai-translator/commit/2b4905f617bd3d153c57b8c9c6da134964461c00))
* **infra:** FocusWatcher via SetWinEventHook (M2 Task 2) ([7eaf4e4](https://github.com/mahdiahmadi1991/ai-translator/commit/7eaf4e4728a5dfae9a207e430459f563f34a2101))
* **infra:** TargetResolver via managed UI Automation (M2 Task 3) ([12c4e3d](https://github.com/mahdiahmadi1991/ai-translator/commit/12c4e3dbabf84dbc18ad1b8f650fc712f0016392))
* **inject:** focus-preserving translation via UIA SetValue (no flicker) ([badf048](https://github.com/mahdiahmadi1991/ai-translator/commit/badf048643bfd43176ac26189dc41cd34ce64593))
* M1 walking skeleton — end-to-end typed translation overlay ([a052eb3](https://github.com/mahdiahmadi1991/ai-translator/commit/a052eb388148bf936f3c2fa16a8995dc3b63ddb7))
* M2 foundation — allowlist policy, hotkey parser, and M2 plan ([bb61688](https://github.com/mahdiahmadi1991/ai-translator/commit/bb6168825a07d87dbafe6785ab58d506fad118c4))
* **overlay:** badge hides while box open; header with settings + direction ([896d7a7](https://github.com/mahdiahmadi1991/ai-translator/commit/896d7a755aea005805c27119e00090f54a57fe8c))
* **overlay:** explicit Translate button, draft preserved, badge on the input ([db25f90](https://github.com/mahdiahmadi1991/ai-translator/commit/db25f903870c7c720304f4ea74af3146a80ed16d))
* **release:** auto-update (Velopack) + GitHub Actions release pipeline ([3f96e85](https://github.com/mahdiahmadi1991/ai-translator/commit/3f96e8547477d1376d5b66fc1c4a0bd93995a798))
* **settings:** professional Grammarly-style tabbed Settings window ([11fd2ec](https://github.com/mahdiahmadi1991/ai-translator/commit/11fd2ec228c2c8b8ea41770156c66d9889edaecc))
* **ui:** drag box by header, caret-to-end after inject, badge hover opacity ([0acb9d0](https://github.com/mahdiahmadi1991/ai-translator/commit/0acb9d0e59d06923c5f6b18a6c1e2a06cd33d185))
* **ui:** fixed-size translate box + polished badge at field's bottom-right ([7d526e2](https://github.com/mahdiahmadi1991/ai-translator/commit/7d526e27fd5a8c0836936888bc03bab6eacd25ab))
* **ui:** Grammarly-style box (header/footer) + badge right-click menu ([d307cb6](https://github.com/mahdiahmadi1991/ai-translator/commit/d307cb61b17626d91ff22536423c889ea6e9c0dc))



