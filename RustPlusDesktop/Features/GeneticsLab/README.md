# Rust Genetics Lab 🌿🧬

A fast, interactive genetics calculator, clone inventory bank, target designer, multi-generation crossbreeding solver, OCR screen scanner, and step-by-step in-game breeding assistant for Rust agriculture.

## ✨ Features

- **Direct Manual Input & Saved Sets**: Fast, zero-friction multi-line plant input with instant circular gene badges (`G`, `Y`, `H`, `W`, `X`), line indices, and auto-saving history.
- **Target Designer & Presets**: Rapidly configure target genetics (`3G 3Y Balanced`, `2G 4Y Max Yield`, `4G 2Y Fast Growth`, etc.).
- **Multi-Generation Crossbreeding Solver**: High-performance multi-threaded solver with Web Workers and cooperative async yielding.
- **Interactive Route Inspector**: Visualizes the 3×3 planter layout with planting order and full lineage dependency trees.
- **Step-by-Step Breeding Assistant**: Live in-game execution mode tracking planting steps, probabilities, and success confirmations.
- **OCR Tooltip Scanner**: Real-time scanner with zoomed surround preview, 6-slot guide stripes calibration, and custom preset import/export.
- **Standalone Web & Desktop Integration**: Runs both as a standalone React 18 web application and inside RustPlusDesktop via WebView2.

## 🚀 Quick Start

### Install Dependencies
```bash
npm install
```

### Run Local Development Server
```bash
npm run dev
```

### Run Unit Tests
```bash
npm run test
```

### Build for Production
```bash
npm run build
```

---

## 🛠️ Tech Stack
- **Framework**: React 18 + TypeScript + Vite
- **UI Components**: Material UI (MUI v6/v9) + Emotion
- **OCR Engine**: Tesseract.js
- **Testing**: Vitest

---

## 📄 License

This project is licensed under the **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)** License.

- ✅ **Free for Personal & Community Use**: You are free to use, modify, share, and build upon this software.
- ❌ **Non-Commercial**: You may **not** sell, paywall, or use this software or derived works for commercial monetization.
- 🔄 **ShareAlike & Open Source**: Any modifications or derivatives must remain open source and distributed under the exact same license terms.

See the [LICENSE](LICENSE) file for details.
