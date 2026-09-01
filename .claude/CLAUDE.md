@../CLAUDE.md

<!-- Este fichero NO tiene contenido propio y no hay que escribir nada dentro: existe solo
     para que omp lea el CLAUDE.md de la raíz, que es el único de los tres agentes que no
     lo alcanza. omp mira dentro de .claude/ y nada más, pero sí expande los imports, así
     que con esa línea le llega la raíz y todo lo que la raíz importe en cadena.

     No lo conviertas en symlink al de la raíz: entonces omp resolvería los imports contra
     .claude/ y perdería todo lo importado (medido el 2026-08-22).

     Y ojo: esto solo funciona si omp se arranca en un directorio que tenga su propio
     .claude/CLAUDE.md, porque no sube por el árbol. Desde un subdirectorio sin puente no
     encuentra nada, ni siquiera el CLAUDE.md que tenga al lado. Si vas a trabajar dentro de
     un subdirectorio concreto, dale su propio puente importando los CLAUDE.md de arriba.

     La explicación completa está en ~/Hub/dotfiles/docs/AGENTES.md. -->
