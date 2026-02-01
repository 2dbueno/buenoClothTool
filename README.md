# buenoClothTool

**An enhanced, optimized, and high-performance fork of [grzyClothTool](https://github.com/grzybeek/grzyClothTool).**

## Overview

**buenoClothTool** is a robust tool designed to create, manage, and preview GTA V addon clothing packs. This fork focuses heavily on **stability, resource management (RAM/VRAM), and data integrity**. It resolves critical issues present in the base version regarding memory leaks during startup, VRAM churn during 3D previews, and logical errors in file deletion synchronization.

If you deal with large clothing projects (1000+ items), this version provides the necessary stability and performance.

## 🚀 Key Technical Enhancements (vs. Original)

This fork introduces significant architectural refactoring to solve common crashes and performance bottlenecks:

### 1. Lazy Loading & RAM Optimization
* **The Problem:** The original tool utilized "Eager Loading," reading bytes from thousands of texture files into memory immediately upon startup. This caused RAM usage to spike to **7GB+** for large projects, often leading to crashes.
* **The Solution:** Implemented a **Lazy Loading Pattern** in the `GTexture` class. Texture details and thumbnails are now loaded asynchronously and on-demand (only when the user clicks/selects an item).
* **Result:** Startup RAM usage reduced by **~90%** (from ~7GB down to ~500MB-800MB), ensuring instant startup times regardless of project size.

### 2. VRAM Management & 3D Preview Caching
* **The Problem:** The 3D Previewer previously reconstructed the entire geometry (`.ydd`) every time a texture was swapped. This caused massive CPU spikes and VRAM "churn," leading to FPS drops and GPU memory leaks over long sessions.
* **The Solution:** Refactored `CWHelper` and `PreviewWindowHost` to implement **Geometry Caching**. The tool now checks if a model is already loaded in the DirectX context before reloading it. Additionally, strictly explicit `Dispose()` calls and garbage collection triggers were added when closing previews to free up GPU resources.

### 3. Data Integrity & "Zombie" Files
* **The Problem:** Deleting an item from the UI sometimes failed to remove it from the internal memory list due to incorrect referencing (`SelectedAddon` vs. actual owner). This caused deleted items to "resurrect" after Autosave/Restart.
* **The Solution:** Rewrote `DeleteDrawables` logic to strictly identify the parent Addon of an item before removal. Added safety locks (`SaveHelper.SavingPaused`) to prevent race conditions between deletion and Autosave, ensuring the `autosave.json` and file system remain perfectly synced.

### 4. Advanced Duplicate Detection (Whitelist)
* **New Feature:** Added a **"Ignore Group / Whitelist"** capability to the Duplicate Inspector.
* **Context:** The tool detects duplicates via Hash (Geometry + Texture count). Sometimes, users intentionally reuse geometry for different items.
* **Implementation:** You can now whitelist specific Hashes. These preferences are persisted in `autosave.json`, preventing false positives from cluttering your workflow in future scans.

## ✨ Core Features

- **Optimized Performance:** Handles projects with 1000+ drawables smoothly.
- **Smart Duplicate Inspector:** Detects identical files via Hash MD5 and allows Whitelisting.
- **Real-time 3D Preview:** Powered by CodeWalker logic, with fixed memory leaks.
- **Auto-Split:** Automatically handles the GTA V 128-item limit by splitting addons.
- **Texture Management:** Easily preview and replace textures without external tools.
- **Safe File Management:** Option to automatically delete physical files when removing items from the project.

## ⚠️ Disclaimer

This software is provided "as is", without warranty of any kind. While extensive measures have been taken to ensure data safety (such as the `DeletePhysicalFilesSafe` implementation), always backup your raw project files before performing batch operations.

## 🤝 Credits & Acknowledgments

This project is a fork of **grzyClothTool** and stands on the shoulders of giants. Huge thanks to the original creators and the modding community:

* **[grzybeek](https://github.com/grzybeek)**: Creator of the original [grzyClothTool](https://github.com/grzybeek/grzyClothTool).
* **[dexyfex](https://github.com/dexyfex/CodeWalker)**: Creator of **CodeWalker**. The 3D preview logic relies heavily on his core libraries. Please [Support dexyfex on Patreon](https://www.patreon.com/dexyfex).
* **[ook](https://github.com/ook3d)** & **[JagodaMods](https://discord.gg/jagoda)**: For their contributions to the original codebase.

## 📄 License

This project is licensed under the **GNU General Public License v3.0**. See the [LICENSE](LICENSE) file for details.