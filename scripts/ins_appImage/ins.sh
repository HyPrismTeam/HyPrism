#!/usr/bin/env bash
set -e

# Check python
if command -v python3 >/dev/null 2>&1; then
    echo "Python installed"
else
    echo "Python not installed. Please install Python 3"
    exit 1
fi

# Check pip
if python3 -m pip --version >/dev/null 2>&1; then
    echo "pip installed"
else
    echo "pip not installed. Please install pip"
    exit 1
fi

#!/bin/bash

# Check if FUSE is supported by the kernel and if fusermount3 is installed
# 1. Check for FUSE3 (System standard)
if ! command -v fusermount3 >/dev/null 2>&1; then
    echo "Missing fuse3."
    exit 3
fi


if ! dpkg -s libfuse2 >/dev/null 2>&1 && ! dpkg -s libfuse2t64 >/dev/null 2>&1; then
    echo "Warning: libfuse2 is missing. Older AppImages might not launch."
fi

echo "FUSE check passed."


# Create venv if not exists
if [ ! -d ".venv" ]; then
    python3 -m venv .venv
fi

# Activate venv
source .venv/bin/activate

# Install deps
python3 -m pip install --upgrade pip
python3 -m pip install requests

# Run app
python3 main.py
