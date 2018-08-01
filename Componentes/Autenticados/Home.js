import React, { Component } from 'react';
import { View, Text, Button } from 'react-native';

export default class Home extends Component {
  render() {
    const { navigation } = this.props;
    return (
      <View style={styles.container}>
        <Text> Home </Text>
        <Button title="Autor" onPress={() => { navigation.navigate('Autor'); }} />
        <Button title="Comentarios" onPress={() => { navigation.navigate('Comentarios'); }} />
      </View>
    );
  }
}

const styles = ({
  container: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: '#2c3e50',
  },
});
