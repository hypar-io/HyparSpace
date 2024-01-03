#!/bin/bash
projects=(
    "ClassroomLayout"
    "DataHall"
    "LoungeLayout"
    "MeetingRoomLayout"
    "OpenCollabLayout"
    "OpenOfficeLayout"
    "PantryLayout"
    "PhoneBoothLayout"
    "PrivateOfficeLayout"
    "ReceptionLayout"
    "InteriorPartitions"
    "Doors"
)

# Initialize an empty array to hold projects that fail to build
declare -a failedProjects

# Function to perform 'dotnet build' and check for errors
task() {
    local project=$1
    echo "Building $project"
    cd "./$project"
    if ! hypar publish --disable-pull-check 2>&1; then
        failedProjects+=("$project")
    fi

    cd "../"
}

# Loop through each project and call the task function
for project in "${projects[@]}"; do
    task "$project"
done

# Report projects that failed to build
if [ ${#failedProjects[@]} -ne 0 ]; then
    echo "Failed to build the following projects:"
    for failed in "${failedProjects[@]}"; do
        echo " - $failed"
    done
else
    echo "All projects built successfully."
fi