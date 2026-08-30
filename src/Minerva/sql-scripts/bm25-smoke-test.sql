SELECT id, pdb.score(id) FROM chunks
WHERE collection_name = 'wikipedia-nollm' AND content ||| 'NFL'
ORDER BY 2 DESC LIMIT 5;