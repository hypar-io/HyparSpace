DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
rm -r "$DIR/Generated"
source ~/.bash_profile

types=(
"https://prod-api.hypar.io/schemas/WorkpointCount"
)
for t in ${types[@]}; do
    hypar generate-types -u $t -o $DIR/Generated
done;