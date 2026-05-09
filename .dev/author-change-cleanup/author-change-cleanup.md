# Author rewrite cleanup

Run next week, after you're confident the rewritten history is fine and you no longer need the backup tags.

```bash
# Remove local backup tags
git tag -d backup/pre-author-rewrite/main backup/pre-author-rewrite/remove-di-refactoring

# Remove leftover filter-repo working data
rm .git/filter-repo-mailmap.txt
rm -rf .git/filter-repo

# Expire reflog and GC the orphaned old commits
git reflog expire --expire=now --all && git gc --prune=now --aggressive
```
