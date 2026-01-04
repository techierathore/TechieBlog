#!/bin/bash
#
# Rename-Project Script for TechieBlog Template
#
# Usage:
#   ./rename-project.sh <NewName>
#   ./rename-project.sh <NewName> --dry-run
#
# Examples:
#   ./rename-project.sh MyBlog
#   ./rename-project.sh DevNotes --dry-run
#
# This script renames the main application from "TechieBlog" to your chosen name.
# The component libraries (BlogUI, BlogEngine, BlogModels, BlogDb) are kept as-is.
#

set -e

OLD_NAME="TechieBlog"
NEW_NAME="$1"
DRY_RUN=false

# Check for dry-run flag
if [[ "$2" == "--dry-run" ]] || [[ "$1" == "--dry-run" ]]; then
    DRY_RUN=true
    if [[ "$1" == "--dry-run" ]]; then
        NEW_NAME="$2"
    fi
fi

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# Validate input
if [[ -z "$NEW_NAME" ]]; then
    echo -e "${RED}Error: Please provide a new project name.${NC}"
    echo ""
    echo "Usage: ./rename-project.sh <NewName> [--dry-run]"
    echo "Example: ./rename-project.sh MyBlog"
    exit 1
fi

# Validate name format (alphanumeric, starting with letter)
if [[ ! "$NEW_NAME" =~ ^[a-zA-Z][a-zA-Z0-9]*$ ]]; then
    echo -e "${RED}Error: Project name must start with a letter and contain only alphanumeric characters.${NC}"
    exit 1
fi

# Ensure we're in the right directory
if [[ ! -f "TechieBlog.slnx" ]]; then
    echo -e "${RED}Error: Please run this script from the repository root directory (where TechieBlog.slnx is located).${NC}"
    exit 1
fi

echo ""
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}  TechieBlog Template Rename Script${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "${YELLOW}Renaming: $OLD_NAME -> $NEW_NAME${NC}"

if $DRY_RUN; then
    echo -e "${MAGENTA}(DRY RUN - No changes will be made)${NC}"
fi
echo ""

CHANGE_COUNT=0

log_change() {
    echo -e "  ${GRAY}$1 : $2${NC}"
    ((CHANGE_COUNT++))
}

# Step 1: Update solution file content
echo -e "${GREEN}[1/6] Updating solution file references...${NC}"
if grep -q "source/$OLD_NAME" TechieBlog.slnx; then
    log_change "UPDATE" "TechieBlog.slnx (project references)"
    if ! $DRY_RUN; then
        if [[ "$OSTYPE" == "darwin"* ]]; then
            # macOS
            sed -i '' "s|source/$OLD_NAME/$OLD_NAME.csproj|source/$NEW_NAME/$NEW_NAME.csproj|g" TechieBlog.slnx
            sed -i '' "s|source/$OLD_NAME/|source/$NEW_NAME/|g" TechieBlog.slnx
        else
            # Linux
            sed -i "s|source/$OLD_NAME/$OLD_NAME.csproj|source/$NEW_NAME/$NEW_NAME.csproj|g" TechieBlog.slnx
            sed -i "s|source/$OLD_NAME/|source/$NEW_NAME/|g" TechieBlog.slnx
        fi
    fi
fi

# Step 2: Update .cs files in TechieBlog project (namespaces)
echo -e "${GREEN}[2/6] Updating namespace references in source files...${NC}"
if [[ -d "source/$OLD_NAME" ]]; then
    find "source/$OLD_NAME" -name "*.cs" -type f 2>/dev/null | while read -r file; do
        if grep -q "namespace $OLD_NAME\|using $OLD_NAME" "$file" 2>/dev/null; then
            log_change "UPDATE" "$file"
            if ! $DRY_RUN; then
                if [[ "$OSTYPE" == "darwin"* ]]; then
                    sed -i '' "s/namespace $OLD_NAME/namespace $NEW_NAME/g" "$file"
                    sed -i '' "s/using $OLD_NAME/using $NEW_NAME/g" "$file"
                else
                    sed -i "s/namespace $OLD_NAME/namespace $NEW_NAME/g" "$file"
                    sed -i "s/using $OLD_NAME/using $NEW_NAME/g" "$file"
                fi
            fi
        fi
    done
fi

# Step 3: Update .razor files
echo -e "${GREEN}[3/6] Updating Razor component references...${NC}"
if [[ -d "source/$OLD_NAME" ]]; then
    find "source/$OLD_NAME" -name "*.razor" -type f 2>/dev/null | while read -r file; do
        if grep -q "@namespace $OLD_NAME\|@using $OLD_NAME" "$file" 2>/dev/null; then
            log_change "UPDATE" "$file"
            if ! $DRY_RUN; then
                if [[ "$OSTYPE" == "darwin"* ]]; then
                    sed -i '' "s/@namespace $OLD_NAME/@namespace $NEW_NAME/g" "$file"
                    sed -i '' "s/@using $OLD_NAME/@using $NEW_NAME/g" "$file"
                else
                    sed -i "s/@namespace $OLD_NAME/@namespace $NEW_NAME/g" "$file"
                    sed -i "s/@using $OLD_NAME/@using $NEW_NAME/g" "$file"
                fi
            fi
        fi
    done
fi

# Step 4: Update configuration files
echo -e "${GREEN}[4/6] Updating configuration files...${NC}"
for config in "source/$OLD_NAME/appsettings.json" "source/$OLD_NAME/appsettings.Development.json" "source/$OLD_NAME/appsettings.Production.json"; do
    if [[ -f "$config" ]] && grep -q "$OLD_NAME" "$config" 2>/dev/null; then
        log_change "UPDATE" "$config"
        if ! $DRY_RUN; then
            if [[ "$OSTYPE" == "darwin"* ]]; then
                sed -i '' "s/$OLD_NAME/$NEW_NAME/g" "$config"
            else
                sed -i "s/$OLD_NAME/$NEW_NAME/g" "$config"
            fi
        fi
    fi
done

# Step 5: Rename project file
echo -e "${GREEN}[5/6] Renaming project file...${NC}"
if [[ -f "source/$OLD_NAME/$OLD_NAME.csproj" ]]; then
    log_change "RENAME" "source/$OLD_NAME/$OLD_NAME.csproj -> source/$OLD_NAME/$NEW_NAME.csproj"
    if ! $DRY_RUN; then
        mv "source/$OLD_NAME/$OLD_NAME.csproj" "source/$OLD_NAME/$NEW_NAME.csproj"
    fi
fi

# Step 6: Rename folders and solution file
echo -e "${GREEN}[6/6] Renaming folders and solution file...${NC}"

# Rename project folder
if [[ -d "source/$OLD_NAME" ]]; then
    log_change "RENAME" "source/$OLD_NAME -> source/$NEW_NAME"
    if ! $DRY_RUN; then
        mv "source/$OLD_NAME" "source/$NEW_NAME"
    fi
fi

# Rename solution file
if [[ -f "$OLD_NAME.slnx" ]]; then
    log_change "RENAME" "$OLD_NAME.slnx -> $NEW_NAME.slnx"
    if ! $DRY_RUN; then
        mv "$OLD_NAME.slnx" "$NEW_NAME.slnx"
    fi
fi

# Summary
echo ""
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}  Summary${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "${YELLOW}Total changes: $CHANGE_COUNT${NC}"

if $DRY_RUN; then
    echo ""
    echo -e "${MAGENTA}This was a DRY RUN. No changes were made.${NC}"
    echo -e "${MAGENTA}Run without --dry-run to apply changes.${NC}"
else
    echo ""
    echo -e "${GREEN}Rename complete!${NC}"
    echo ""
    echo -e "${YELLOW}Next steps:${NC}"
    echo "  1. Open $NEW_NAME.slnx in your IDE"
    echo "  2. Build to verify: dotnet build"
    echo "  3. Run: dotnet run --project source/$NEW_NAME"
    echo "  4. Delete this scripts folder if no longer needed"
fi

echo ""
