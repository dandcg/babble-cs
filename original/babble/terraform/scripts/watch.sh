 
 #!/bin/bash

watch -n 1 '
cat ips.dat  | \
awk '"'"'{print $2}'"'"' | \
xargs -I % curl -s http://%:8080/Stats |\
tr -d "{}\"" | \
awk -F "," '"'"'{gsub (/[,]/," "); print;}'"'"'
'

 
