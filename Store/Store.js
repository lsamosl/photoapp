import { createStore, combineReducers, applyMiddleware } from 'redux';
import { reducer as form } from 'redux-form';
import createSagaMiddleware from 'redux-saga';
import funcionPrimaria from '../Sagas/Sagas';

const reducerPrueba = (state = [0], action) => {
  switch (action.type) {
    case 'AUMENTAR_REDUCER_PRUEBA':
      return [...state, 1];
    /*
    case 'REGISTRO':
      console.log('Ejecucion desde el reducer');
      return state;
    */
    default:
      return state;
  }
};

const sagaMiddleware = createSagaMiddleware();

const reducers = combineReducers({
  reducerPrueba,
  form,
});

const store = createStore(reducers, applyMiddleware(sagaMiddleware));

sagaMiddleware.run(funcionPrimaria);

/*
const miMiddleware = store => next => (action) => {
  console.log('Se ejecuta el middleware');
  next(action);
};

const ultimoMiddleware = store => next => (action) => {
  console.log('ultimo middleware');
  next(action);
};

const store = createStore(reducers, applyMiddleware(miMiddleware, ultimoMiddleware));
*/

export default store;
