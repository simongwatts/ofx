# OFX - Ollama Function eXecutor (C# Edition)

[![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)](https://dotnet.microsoft.com)
[![Ollama](https://img.shields.io/badge/Ollama-white)](https://ollama.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**OFX** is a powerful cross-platform CLI tool for interacting with Ollama models. Designed for pipeline integration and AI-powered automation, it provides clean, formatted outputs perfect for scripting and downstream processing.

## Features

- 🚀 Direct Ollama API integration
- ⚙️ Customizable model parameters (temperature, context window, etc.)
- 📋 System prompt management for consistent behavior
- 🔍 Verbose mode for debugging and configuration inspection
- 🛠️ Automatic option binding from Ollama API schema
- 🚫 No extra dependencies (single binary deployment)

## Installation

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Ollama](https://ollama.com/)

### Quick Start
# Clone repository
git clone https://github.com/simongwatts/ofx.git

cd ofx

cd ofx

# Build and install
dotnet publish -c Release -o ./publish

# Run
cd publish

ofx --help

ofx "If I have 3 apples and eat 2, how many bananas do I have" --temperature 0.8 --verbose