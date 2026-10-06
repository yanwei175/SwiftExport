
-- 只读核对指定PG库public结构；不读取凭据、业务数据或序列当前值，不执行DDL或写入。
WITH objects AS (
SELECT 'columns' kind,c.relname||':'||a.attnum key,concat_ws('|',a.attname,format_type(a.atttypid,a.atttypmod),a.attnotnull,a.attidentity,a.attgenerated,CASE WHEN a.attcollation=0 THEN '' ELSE a.attcollation::regcollation::text END,pg_get_expr(d.adbin,d.adrelid)) definition
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped LEFT JOIN pg_attrdef d ON d.adrelid=c.oid AND d.adnum=a.attnum WHERE n.nspname='public' AND c.relkind IN('r','p','v','m')
UNION ALL SELECT 'constraints',c.relname||':'||k.conname,concat_ws('|',k.contype,k.condeferrable,k.condeferred,k.convalidated,pg_get_constraintdef(k.oid,true)) FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
UNION ALL SELECT 'indexes',c.relname,pg_get_indexdef(c.oid) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='i'
UNION ALL SELECT 'triggers',c.relname||':'||t.tgname,concat_ws('|',t.tgenabled,pg_get_triggerdef(t.oid,true)) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND NOT t.tgisinternal
UNION ALL SELECT 'functions',p.proname||'('||pg_get_function_identity_arguments(p.oid)||')',pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.prokind IN('f','p')
UNION ALL SELECT 'collations',c.collname,concat_ws('|',c.collprovider,c.collisdeterministic,c.collencoding,c.collcollate,c.collctype,c.colllocale,c.collversion) FROM pg_collation c JOIN pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public'
UNION ALL SELECT 'sequences',c.relname,concat_ws('|',format_type(s.seqtypid,NULL),s.seqstart,s.seqincrement,s.seqmax,s.seqmin,s.seqcache,s.seqcycle) FROM pg_sequence s JOIN pg_class c ON c.oid=s.seqrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
UNION ALL SELECT 'views',c.relname,pg_get_viewdef(c.oid,true) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN('v','m')
UNION ALL SELECT 'extensions',extname,extversion FROM pg_extension
UNION ALL SELECT 'relations',c.relname,concat_ws('|',c.relkind,c.relpersistence,c.relrowsecurity,c.relforcerowsecurity,array_to_string(c.reloptions,',')) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN('r','p','v','m')
)
SELECT kind,count(*) object_count,md5(string_agg(key||'='||definition,chr(10) ORDER BY key)) definition_md5 FROM objects GROUP BY kind ORDER BY kind
