;; This simple lisp expression references a lisp symbol (lsp),
;; that was assigned to the value of an MSBUILD property by the 
;; generated loader script (the full path to this .lsp file).
;;
;; Hence, the loader script boilerplate can be extended to assign
;; other build properties or any value from the build envioronment
;; to LISP symbols as well, which can be referenced from this file. 

(princ (strcat "\nLoaded " lsp))