#!/bin/bash

# Find all hypar.json files, excluding those in Archive, and store their paths in an array
file_paths=($(find -name 'hypar.json' ! -path '*/Archive/*'))

# Array to store failed directories
failed_directories=()

# Iterate through each file path
for file_path in "${file_paths[@]}"; do
  # Get the directory containing the hypar.json file
  directory=$(dirname "$file_path")

  # Change to the directory
  cd "$directory" || exit

  # Run dotnet build
  if ! dotnet build; then
    # Add the failed directory to the array
    failed_directories+=("$directory")
  fi

  # Change back to the original directory
  cd - || exit
done

# Check if there are any failed directories
if [ ${#failed_directories[@]} -eq 0 ]; then
  echo "All builds successful"
else
  echo "Failed directories:"
  for failed_directory in "${failed_directories[@]}"; do
    echo "$failed_directory"
  done
fi