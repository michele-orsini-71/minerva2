SELECT locktype, classid, objid, pid, mode, granted FROM pg_locks WHERE locktype = 'advisory';

SELECT l.pid, l.mode, l.granted, (l.classid::bigint << 32) | l.objid::bigint AS lock_id, a.application_name, a.state, a.query FROM pg_locks l LEFT JOIN pg_stat_activity a ON a.pid = l.pid  WHERE l.locktype = 'advisory';