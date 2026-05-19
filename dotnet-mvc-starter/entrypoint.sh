#!/bin/sh
set -e

echo "Starting application as non-root..."
exec su-exec appuser dotnet dotnet-mvc-starter.dll
