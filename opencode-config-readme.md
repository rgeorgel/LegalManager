# OpenCode Configuration

This directory contains the configuration for [OpenCode AI](https://opencode.ai), an AI-powered coding assistant.

## Overview

OpenCode is configured as a plugin-based AI coding assistant that uses the MiniMax provider with the `MiniMax-M2.7` model. The configuration is defined in `opencode.json`.

## Configuration Files

### `opencode.json`
The main configuration file specifying:
- **Plugin**: `opencode-tps-meter` for tracking tokens per second
- **Provider**: MiniMax AI
- **Model**: `MiniMax-M2.7` with high reasoning effort
- **Variants**: A `focused` variant with high reasoning effort

### `package.json`
Project dependencies including `@opencode-ai/plugin` v1.14.21

## Key Settings

| Setting | Value |
|---------|-------|
| Model | MiniMax-M2.7 |
| Reasoning Effort | xhigh (extremely high) |
| Plugin | opencode-tps-meter |
| OpenCode Version | 1.14.21 |

## Model Variants

The `MiniMax-M2.7` model has a `focused` variant that also uses `xhigh` reasoning effort for tasks requiring deep concentration.

## Dependencies

- `@opencode-ai/plugin` (1.14.21)