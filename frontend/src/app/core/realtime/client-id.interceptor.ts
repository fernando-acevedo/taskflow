import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { CLIENT_ID_HEADER, clientId } from './client-id';

const apiPrefix = `${environment.apiUrl}/api/`;

/** Sends `X-Client-Id` with every API call (ADR 0023): the hub tags the events a call causes with it. */
export const clientIdInterceptor: HttpInterceptorFn = (request, next) =>
  request.url.startsWith(apiPrefix) ? next(request.clone({ setHeaders: { [CLIENT_ID_HEADER]: clientId() } })) : next(request);
