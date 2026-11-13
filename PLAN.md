# FileShift · Plan de remediación para la primera release

- [x] Preflight. Confirmar seis commits, integridad Git, ramas y estado remoto. Revisar residuos del archivo original y detectar patrones de credenciales en el historial.
- [ ] P0. Validar todas las salidas antes de publicar. Confirmar la regresión PDF, rollback parcial y comportamiento de Overwrite, Skip y AutoRename con el original intacto.
- [ ] P1. Verificar selección explícita de destino, cancelación tras finalizar motores, cambios de concurrencia, cierre de cola, persistencia de preferencias y automatizaciones en segundo plano.
- [ ] Explorer. Verificar matriz de formatos y motores, argumentos Unicode, COM y carga de la extensión desde la instalación real.
- [ ] Distribución. Compilar, probar y empaquetar en Actions para Windows x64. Verificar el MSIX y el instalador de desarrollo. Documentar la firma necesaria para distribución general.
- [ ] QA físico. Ejecutar los escenarios de HANDOFF en Windows 11 y revisar el candidato antes de decidir la release.
