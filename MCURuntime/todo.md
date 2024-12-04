1> add polymophism support: 
	1> heap object add a "base referenc id", null for none.
	2> process.cs: each instance-able class have a "base cls-id" -1 for it's root.
2> aligned stack?
3> heap: pure value array
array of struct should initialize( not supported right now.)
function return valuetype is not treated properlly.
how to freely/cheaply add internal functions?