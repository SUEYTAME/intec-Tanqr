import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { PublicTicket } from './PublicTicket.tsx'
import './App.css'
import './screens.css'

// /ticket/<id>.<token> es el enlace público del ticket (RF-09) y no requiere sesión.
// El formato <32 hex>.<token de 22> es el que genera PublicLinks en el backend.
const ticketKey =
  /^\/ticket\/([0-9a-f]{32}\.[A-Za-z0-9_-]{22})\/?$/.exec(window.location.pathname)?.[1] ?? null

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {ticketKey ? <PublicTicket ticketKey={ticketKey} /> : <App />}
  </StrictMode>,
)
