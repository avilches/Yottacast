@../CLAUDE.md

<!-- Este fichero NO tiene contenido propio y no hay que escribir nada dentro: existe solo
     para que omp lea el CLAUDE.md de la raíz, que es el único de los tres agentes que no
     lo alcanza. omp mira dentro de .claude/ y nada más, pero sí expande los imports, así
     que con esa línea le llega la raíz y todo lo que la raíz importe en cadena.

     No lo conviertas en symlink al de la raíz: entonces omp resolvería los imports contra
     .claude/ y perdería todo lo importado (medido el 2026-08-22).

     Y ojo: esto solo funciona si omp se arranca en la raíz del repositorio. Desde un
     subdirectorio no encuentra este fichero y se queda sin las instrucciones del proyecto.

     La explicación completa, con la matriz de qué lee cada agente, está en la sección
     "Los tres agentes leen las mismas instrucciones" del CLAUDE.md de ~/Hub/dotfiles. -->
